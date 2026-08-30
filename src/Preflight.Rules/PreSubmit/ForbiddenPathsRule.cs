namespace Preflight.Rules;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Fails when a changed file matches a path pattern the production forbids.
/// </summary>
/// <remarks>
/// Binaries, secrets and local configuration. The patterns are policy rather
/// than code, because what counts as "must not be committed" is a production's
/// decision and differs between them.
/// </remarks>
public sealed class ForbiddenPathsRule : IValidationRule
{
    /// <summary>
    /// The patterns applied when the policy states none.
    /// </summary>
    /// <remarks>
    /// A default that catches the three categories that matter, so the rule is
    /// useful before anyone configures it. Kept short on purpose: a long
    /// default list is a policy decision smuggled into code, and every entry
    /// somebody did not choose is an entry they will disable rather than
    /// understand.
    /// </remarks>
    public static readonly string[] DefaultPatterns =
    [
        "**/*.pfx",
        "**/*.p12",
        "**/id_rsa",
        "**/.env",
        "**/*.local.json",
    ];

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.ForbiddenPaths,
        DisplayName = "Forbidden path",
        Stage = ValidationStage.PreSubmit,
        DefaultBlocking = true,
        DefaultGating = false,
    };

    public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var patterns = context.Policy.GetValue("patterns", DefaultPatterns);

        // No patterns is not the same as no files. The rule was configured to
        // forbid nothing, so it examined nothing, and reporting a tick would
        // claim a check that never ran.
        if (patterns.Length == 0 || context.ChangedFiles.Count == 0)
        {
            return Task.FromResult(RuleOutcome.NotApplicable());
        }

        var globs = Array.ConvertAll(patterns, GlobPattern.Compile);
        var scan = new ChangedFileScan();

        foreach (var file in context.ChangedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!scan.Examines(file))
            {
                continue;
            }

            if (Array.Find(globs, glob => glob.Matches(file.RelativePath)) is { } matched)
            {
                // One finding per file, not one per matching pattern. Two
                // overlapping patterns describe one problem, and reporting it
                // twice makes the count in the summary line disagree with the
                // number of files a reader has to fix.
                scan.Report(Describe(file.RelativePath, matched.Text));
            }
        }

        return Task.FromResult(scan.Outcome());
    }

    /// <remarks>
    /// The finding names the path and the pattern, never the content. This rule
    /// and the compile probe are the two places file content could reach the
    /// report, and from there a build log and the run's stored history — so
    /// quoting the line a secret sits on would publish it to everyone who can
    /// read a build.
    /// </remarks>
    private static Finding Describe(string relativePath, string pattern) => new()
    {
        Message = "Changed file matches a forbidden path pattern.",
        Location = new FindingLocation(relativePath),
        Expected = "no changed file matching a forbidden pattern",
        Actual = $"matches '{pattern}'",
        Remediation =
            "Remove the file from the change. If it belongs in the repository, " +
            "adjust 'patterns' for this rule in the pipeline's policy.",
    };
}
