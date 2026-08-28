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
    /// of configuration. The worked example in the docs moves it to 50 MB for a
    /// production with large assets, which is the point of it being policy.
    /// </remarks>
    public const long DefaultMaxBytes = 5 * 1024 * 1024;

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.LargeFile,
        DisplayName = "Large changed file",
        Stage = ValidationStage.PreSubmit,
        DefaultBlocking = true,

        // Gating is false on the leaves. Nothing depends on this
        // rule, so the value is irrelevant there — stated explicitly so nobody
        // reads the descriptor's own `true` default as meaning something.
        DefaultGating = false,
    };

    public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var maxBytes = context.Policy.GetValue("maxBytes", DefaultMaxBytes);
        var findings = new List<Finding>();
        var examined = 0;

        foreach (var file in context.ChangedFiles)
        {
            // This is the rule author's contract rather than a
            // style point: a pre-submit rule can receive tens of thousands of
            // entries, and one that never checks the token cannot be stopped.
            cancellationToken.ThrowIfCancellationRequested();

            // A deleted file has no size to measure, and asking for one throws.
            // Filtering here rather than at the size call is what makes a commit
            // that only deletes things report n/a: nothing was examined.
            if (file.Kind == ChangeKind.Deleted)
            {
                continue;
            }

            examined++;

            // The new path, never the old one. A rename's PreviousRelativePath
            // names a file that no longer exists.
            var size = context.FileSystem.GetFileSize(
                Path.Combine(context.WorkspaceRoot.FullName, file.RelativePath));

            if (size > maxBytes)
            {
                findings.Add(Describe(file.RelativePath, size, maxBytes));
            }
        }

        return Task.FromResult(Outcome(examined, findings));
    }

    private static RuleOutcome Outcome(int examined, List<Finding> findings) => examined switch
    {
        // Stated as a rule rather than as a convenience: nothing was
        // examined, so there is nothing to say. Passed here would be a small
        // lie, and small lies in a validation report erode
        // trust in the whole thing.
        0 => RuleOutcome.NotApplicable(),
        _ when findings.Count > 0 => RuleOutcome.Failed([.. findings]),
        _ => RuleOutcome.Passed(),
    };

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
