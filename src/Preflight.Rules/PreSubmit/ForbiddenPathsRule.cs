namespace Preflight.Rules;

using System.Text.RegularExpressions;
using Preflight.Abstractions;

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

    /// <summary>
    /// Translates one glob into a regular expression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>**</c> crosses directory separators and <c>*</c> does not, which is
    /// the distinction the whole pattern language turns on: <c>*.pfx</c> means
    /// a certificate at the root, <c>**/*.pfx</c> means one anywhere.
    /// Collapsing them would make every pattern accidentally recursive.
    /// </para>
    /// <para>
    /// Matching is case-insensitive. Windows and macOS filesystems are, so a
    /// case-sensitive matcher would let <c>Secrets/KEY.PFX</c> through on the
    /// machines most developers use — and letting a secret through is the
    /// direction of error that matters here.
    /// </para>
    /// </remarks>
    internal static Regex Compile(string pattern)
    {
        var expression = new System.Text.StringBuilder("^");

        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];

            if (character == '*')
            {
                if (index + 1 < pattern.Length && pattern[index + 1] == '*')
                {
                    // '**/' also matches zero directories, so '**/*.pfx' catches
                    // a certificate at the root as well as a nested one. Without
                    // that, every pattern would need writing twice.
                    index++;

                    if (index + 1 < pattern.Length && pattern[index + 1] == '/')
                    {
                        index++;
                        expression.Append("(?:.*/)?");

                        continue;
                    }

                    expression.Append(".*");

                    continue;
                }

                expression.Append("[^/]*");

                continue;
            }

            expression.Append(character == '?' ? "[^/]" : Regex.Escape(character.ToString()));
        }

        expression.Append('$');

        return new Regex(
            expression.ToString(),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var patterns = context.Policy.GetValue("patterns", DefaultPatterns);

        // No patterns is not the same as no files. The rule was configured to
        // forbid nothing, so it examined nothing, and the reasoning for
        // NotApplicable applies unchanged.
        if (patterns.Length == 0 || context.ChangedFiles.Count == 0)
        {
            return Task.FromResult(RuleOutcome.NotApplicable());
        }

        var compiled = patterns.Select(pattern => (Pattern: pattern, Regex: Compile(pattern))).ToArray();
        var findings = new List<Finding>();
        var examined = 0;

        foreach (var file in context.ChangedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Deleting a forbidden file is the fix, not the violation. A rule
            // that failed here would tell someone their cleanup commit is the
            // problem, and there would be no commit that satisfies it.
            if (file.Kind == ChangeKind.Deleted)
            {
                continue;
            }

            examined++;

            foreach (var (pattern, regex) in compiled)
            {
                if (regex.IsMatch(file.RelativePath))
                {
                    findings.Add(Describe(file.RelativePath, pattern));

                    // One finding per file, not one per matching pattern. Two
                    // overlapping patterns describe one problem, and reporting
                    // it twice makes the count in the summary line wrong.
                    break;
                }
            }
        }

        return Task.FromResult(Outcome(examined, findings));
    }

    private static RuleOutcome Outcome(int examined, List<Finding> findings) => examined switch
    {
        0 => RuleOutcome.NotApplicable(),
        _ when findings.Count > 0 => RuleOutcome.Failed([.. findings]),
        _ => RuleOutcome.Passed(),
    };

    /// <remarks>
    /// The finding names the path and the pattern, never the content. This rule
    /// is one of the two places a secret could enter the report, and from there
    /// a CI log — and, from the history, the NDJSON history.
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
