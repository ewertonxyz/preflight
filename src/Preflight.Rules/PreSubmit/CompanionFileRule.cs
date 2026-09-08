namespace Preflight.Rules;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Fails when a changed file that matches a pattern arrived without the
/// partner the policy requires beside it.
/// </summary>
/// <remarks>
/// <para>
/// The pairs are policy and nothing about any particular tool or asset format
/// is written here. That is what keeps the rule honest: a studio whose importer
/// writes a sidecar next to every asset declares the pair in one line, and a
/// studio whose importer does not never sees the rule at all.
/// </para>
/// <para>
/// No default pairs, and that is a decision rather than an omission. Which
/// files travel in pairs is a fact about a production's content pipeline, and
/// a plausible-looking default here would be a production's policy written in
/// C# — which somebody would eventually turn the whole rule off to escape.
/// </para>
/// </remarks>
public sealed class CompanionFileRule : IValidationRule
{
    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.CompanionFile,
        DisplayName = "Companion file",
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

        cancellationToken.ThrowIfCancellationRequested();

        var entries = context.Policy.GetValue("pairs", Array.Empty<string>());

        if (entries.Length == 0)
        {
            return Task.FromResult(RuleOutcome.NotApplicable());
        }

        var malformed = entries.Where(entry => CompanionRequirement.Parse(entry) is null).ToArray();

        if (malformed.Length > 0)
        {
            // Against the policy, before any file is examined. A typo reported
            // once beats the same typo reported against every asset that
            // happened not to match it.
            return Task.FromResult(RuleOutcome.Failed(Misconfigured(malformed)));
        }

        var requirements = entries.Select(entry => CompanionRequirement.Parse(entry)!).ToArray();

        // What the change set already names, so that a pair arriving together
        // costs no syscall at all. Deletions are excluded: being named in a
        // commit that removes a file is not the same as existing.
        var arriving = context.ChangedFiles
            .Where(file => file.Kind != ChangeKind.Deleted)
            .Select(file => file.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scan = new ChangedFileScan();

        foreach (var file in context.ChangedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var matched = requirements.Where(requirement => requirement.When.Matches(file.RelativePath)).ToArray();

            if (matched.Length == 0 || !scan.Examines(file))
            {
                continue;
            }

            foreach (var requirement in matched)
            {
                var companion = requirement.Resolve(file.RelativePath);

                if (arriving.Contains(companion))
                {
                    continue;
                }

                if (!context.FileSystem.FileExists(Path.Combine(context.WorkspaceRoot.FullName, companion)))
                {
                    scan.Report(Describe(file.RelativePath, companion, requirement.Text));
                }
            }
        }

        return Task.FromResult(scan.Outcome());
    }

    /// <remarks>
    /// Names the setting and the entry rather than a file, because nothing is
    /// wrong with the workspace — pointing at an asset would send the reader to
    /// fix something that is perfectly correct.
    /// </remarks>
    private static Finding Misconfigured(IReadOnlyList<string> entries) => new()
    {
        Message = "An entry in 'pairs' is not a companion rule.",
        Expected = $"'<pattern> {CompanionRequirement.Arrow} <template>', with neither side empty",
        Actual = string.Join(", ", entries),
        Remediation = "Correct 'pairs' for this rule in the pipeline's policy.",
    };

    private static Finding Describe(string relativePath, string companion, string pair) => new()
    {
        Message = "A changed file arrived without the companion the policy requires.",
        Location = new FindingLocation(relativePath),
        Expected = $"'{companion}' beside it",
        Actual = "no such file, in the change or in the workspace",
        Remediation =
            $"Add '{companion}', or ask the pipeline's author to change the '{pair}' entry " +
            "in 'pairs' for this rule.",
    };
}
