namespace Preflight.Rules;

using System.Globalization;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Fails when a changed file is larger than the policy allows.
/// </summary>
/// <remarks>
/// The rule that makes <c>NotApplicable</c> worth having: a commit touching
/// only <c>.md</c> files gives it nothing to measure, and reporting a tick
/// there would claim more than is known.
/// </remarks>
public sealed class LargeFileRule : IValidationRule
{
    /// <summary>
    /// The limit when the policy states none: 5 MB.
    /// </summary>
    /// <remarks>
    /// A number that has to exist, because a rule cannot refuse to run for want
    /// of configuration. It is low enough to catch an asset committed by
    /// accident and low enough to annoy a production that ships large ones on
    /// purpose — which is the point of it being policy: such a production moves
    /// it to 50 MB in one line and everything else about the rule stays put.
    /// </remarks>
    public const long DefaultMaxBytes = 5 * 1024 * 1024;

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.LargeFile,
        DisplayName = "Large changed file",
        Stage = ValidationStage.PreSubmit,
        DefaultBlocking = true,

        // Nothing depends on this rule, so gating would change nothing whatever
        // it said. Written out anyway, because the descriptor's own default is
        // true and a reader finding it inherited cannot tell a decision from an
        // omission.
        DefaultGating = false,
    };

    public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var maxBytes = context.Policy.GetValue("maxBytes", DefaultMaxBytes);
        var scan = new ChangedFileScan();

        foreach (var file in context.ChangedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!scan.Examines(file))
            {
                continue;
            }

            // The new path, never the old one. A rename's PreviousRelativePath
            // names a file that no longer exists.
            var size = context.FileSystem.GetFileSize(
                Path.Combine(context.WorkspaceRoot.FullName, file.RelativePath));

            if (size > maxBytes)
            {
                scan.Report(Describe(file.RelativePath, size, maxBytes));
            }
        }

        return Task.FromResult(scan.Outcome());
    }

    /// <remarks>
    /// Both numbers are grouped and both are stated. "Exceeds the limit" alone
    /// leaves the reader working out by how much, and whether the fix is to
    /// move one asset or to raise a limit that was set too low for what this
    /// production ships.
    /// </remarks>
    private static Finding Describe(string relativePath, long size, long maxBytes) => new()
    {
        Message = "Changed file exceeds the size limit.",
        Location = new FindingLocation(relativePath),
        Expected = $"at most {maxBytes.ToString("N0", CultureInfo.InvariantCulture)} bytes",
        Actual = $"{size.ToString("N0", CultureInfo.InvariantCulture)} bytes",
        Remediation =
            "Move the file out of version control, or ask the pipeline's author to " +
            "raise 'maxBytes' for this rule if the size is intended.",
    };
}
