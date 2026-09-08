namespace Preflight.Rules;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;

/// <summary>
/// Checks that every tool the workspace declares is installed and at an
/// accepted version.
/// </summary>
/// <remarks>
/// The root of the workspace stage and, through <c>gating: true</c>, the rule
/// whose failure makes everything downstream pointless: nothing else can be
/// true about a build if the compiler that would produce it is not there.
/// </remarks>
public sealed class ToolchainRule : IValidationRule
{
    /// <remarks>
    /// Enough for a version banner and the first line of an error, and short
    /// enough that a report listing several missing tools still fits a
    /// terminal. A tool asked for its version answers in one line when it
    /// works; everything longer is it explaining why it did not.
    /// </remarks>
    private const int VersionBannerLimit = 200;

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.Toolchain,
        DisplayName = "Toolchain",
        Stage = ValidationStage.Workspace,
        DefaultBlocking = true,

        // Gating, and here it decides something: with no compiler installed,
        // nothing downstream can produce a verdict worth reading, so running it
        // spends time to manufacture noise. Everything in the workspace and
        // build stages hangs off this rule for that reason.
        DefaultGating = true,
    };

    /// <summary>
    /// Reads a version out of whatever a tool printed.
    /// </summary>
    /// <param name="output">Everything the tool wrote to standard output.</param>
    /// <returns>The version, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// Delegates to the collaborator that now owns running a tool and reading
    /// its answer, so that this rule and the platform SDK rule cannot drift
    /// into disagreeing about what a version is. Kept here because it was
    /// already part of this type's surface, and a second implementation is
    /// exactly what the extraction removed.
    /// </remarks>
    public static Version? ParseVersion(string output) => ToolProbe.ParseVersion(output);

    public async Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var read = await WorkspaceManifestRead.ReadAsync(context, cancellationToken);

        if (read.Malformed is { } malformed)
        {
            return RuleOutcome.Failed(malformed);
        }

        // A missing manifest fails rather than reporting n/a, and the choice is
        // deliberate. NotApplicable here is a trapdoor: a mistyped
        // 'manifestPath' would make the rule green forever, and a rule that is
        // permanently green is worse than one that is absent, because it is
        // counted as evidence.
        if (read.Manifest is not { } manifest)
        {
            return RuleOutcome.Failed(new Finding
            {
                Message = "The workspace manifest is missing.",
                Location = new FindingLocation(read.RelativePath),
                Expected = "a manifest declaring the tools this workspace needs",
                Actual = "no file at that path",
                Remediation =
                    $"Add {WorkspaceManifest.DefaultFileName} at the workspace root, " +
                    "or ask the pipeline's author to set 'manifestPath' for this rule.",
            });
        }

        // A manifest that is present and declares no tools is a different fact:
        // somebody said, in writing, that there is nothing to check.
        if (manifest.Tools.Count == 0)
        {
            return RuleOutcome.NotApplicable();
        }

        var findings = new List<Finding>();

        foreach (var tool in manifest.Tools)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await CheckAsync(context, tool, cancellationToken) is { } finding)
            {
                findings.Add(finding);
            }
        }

        return findings.Count > 0 ? RuleOutcome.Failed([.. findings]) : RuleOutcome.Passed();
    }

    private static async Task<Finding?> CheckAsync(
        RuleContext context,
        ToolRequirement tool,
        CancellationToken cancellationToken)
    {
        var probe = await ToolProbe.RunAsync(
            context.Processes,
            new ProcessRequest
            {
                FileName = tool.Command,
                Arguments = tool.Arguments,
                WorkingDirectory = context.WorkspaceRoot.FullName,
            },
            cancellationToken);

        return probe.Status switch
        {
            ToolProbeStatus.Unavailable => Missing(tool, probe.Detail),
            ToolProbeStatus.Unreadable => Unreadable(tool, probe.Detail),

            // Found, and the version is non-null on that arm by construction of
            // the result. The discard carries it rather than a fourth arm,
            // because a fourth arm would be a branch no input can take.
            _ => OutOfRange(tool, probe.Version!),
        };
    }

    private static Finding Unreadable(ToolRequirement tool, string output) => new()
    {
        Message = $"Could not read a version from '{tool.Name}'.",
        Expected = "a version number on the first line of output",
        Actual = FindingText.Truncate(output, VersionBannerLimit),
        Remediation = $"Check that '{tool.Command} {string.Join(' ', tool.Arguments)}' prints a version.",
    };

    private static Finding? OutOfRange(ToolRequirement tool, Version version)
    {
        var minimum = Parse(tool.MinimumVersion);
        var maximum = Parse(tool.MaximumVersion);

        // Below the floor, or at or above the ceiling. The ceiling is exclusive
        // because "anything in 10.x" is written 10.0.0 to 11.0.0, and an
        // inclusive one would need a version nobody can write down.
        if ((minimum is null || version >= minimum) && (maximum is null || version < maximum))
        {
            return null;
        }

        return new Finding
        {
            Message = $"'{tool.Name}' is outside the accepted version range.",
            Expected = Describe(minimum, maximum),
            Actual = version.ToString(),
            Remediation = $"Install '{tool.Name}' at a version inside the accepted range.",
        };
    }

    private static Version? Parse(string? value) =>
        value is not null && Version.TryParse(value, out var version) ? version : null;

    /// <remarks>
    /// Written as nested conditionals rather than a tuple switch because the
    /// switch needs a both-null arm that nothing can reach: with neither bound
    /// set, every version is in range and this is never called. An arm no input
    /// can take is a permanent hole in the branch count, and the usual way that
    /// hole gets closed is a test written to reach it rather than to check
    /// anything.
    /// </remarks>
    private static string Describe(Version? minimum, Version? maximum) =>
        minimum is null
            ? $"below {maximum}"
            : maximum is null
                ? $"at least {minimum}"
                : $"at least {minimum}, below {maximum}";

    private static Finding Missing(ToolRequirement tool, string detail) => new()
    {
        Message = $"'{tool.Name}' is not available.",
        Expected = $"'{tool.Command}' on PATH",
        Actual = detail.Length == 0 ? "the command could not be run" : FindingText.Truncate(detail, VersionBannerLimit),
        Remediation = $"Install '{tool.Name}' and make sure '{tool.Command}' is on PATH.",
    };
}
