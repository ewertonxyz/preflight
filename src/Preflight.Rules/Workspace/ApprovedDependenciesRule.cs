namespace Preflight.Rules;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Fails when the workspace declares a dependency that is not on the list of
/// ids and versions the policy approves.
/// </summary>
/// <remarks>
/// <para>
/// A set comparison, offline, with no feed consulted. What is being checked is
/// not whether a package exists but whether somebody agreed to it, and that
/// question has an answer on a train.
/// </para>
/// <para>
/// Kept apart from the rule that checks dependencies are declared and restored,
/// and it fails rather than warning. The two look alike and are not: an
/// unrestored dependency is fixed by a command nobody has to think about, and
/// an unapproved one is fixed by talking to whoever maintains the list. A
/// warning for the second would put it in the quadrant of a report that readers
/// learn to scroll past, which is where a licensing or security decision is
/// least likely to be seen.
/// </para>
/// </remarks>
public sealed class ApprovedDependenciesRule : IValidationRule
{
    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.ApprovedDependencies,
        DisplayName = "Approved dependencies",
        Stage = ValidationStage.Workspace,
        DependsOn = [BuiltInRuleIds.Toolchain],
        DefaultBlocking = true,

        // Nothing depends on this rule, so gating would change nothing whatever
        // it said. Written out anyway, because the descriptor's own default is
        // true and a reader finding it inherited cannot tell a decision from an
        // omission.
        DefaultGating = false,
    };

    public async Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var read = await WorkspaceManifestRead.ReadAsync(context, cancellationToken);

        if (read.Malformed is { } malformed)
        {
            return RuleOutcome.Failed(malformed);
        }

        // A missing manifest is the toolchain rule's to report, and it does so
        // loudly on the same file.
        if (read.Manifest is not { Dependencies.Count: > 0 } manifest)
        {
            return RuleOutcome.NotApplicable();
        }

        var entries = context.Policy.GetValue("approved", Array.Empty<string>());

        // An empty list is not "approve nothing". Read that way it would fail
        // every dependency in the workspace, and nobody writes an empty array
        // meaning that.
        if (entries.Length == 0)
        {
            return RuleOutcome.NotApplicable();
        }

        var malformedEntries = entries.Where(entry => ApprovedDependency.Parse(entry) is null).ToArray();

        if (malformedEntries.Length > 0)
        {
            return RuleOutcome.Failed(Misconfigured(malformedEntries));
        }

        var approved = entries.Select(entry => ApprovedDependency.Parse(entry)!).ToArray();
        var findings = new List<Finding>();

        foreach (var dependency in manifest.Dependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Array.Exists(approved, entry => entry.Matches(dependency)))
            {
                findings.Add(Describe(dependency));
            }
        }

        return findings.Count > 0 ? RuleOutcome.Failed([.. findings]) : RuleOutcome.Passed();
    }

    /// <remarks>
    /// Names the setting and the entry rather than a dependency, because the
    /// workspace is fine — it is the list that cannot be read.
    /// </remarks>
    private static Finding Misconfigured(IReadOnlyList<string> entries) => new()
    {
        Message = "An entry in 'approved' is not an id and a version.",
        Expected = $"'<id>@<version>', or '<id>@{ApprovedDependency.AnyVersion}' for any version",
        Actual = string.Join(", ", entries),
        Remediation = "Correct 'approved' for this rule in the pipeline's policy.",
    };

    /// <remarks>
    /// The remedy is a conversation and never a command, which is the whole
    /// distinction from the rule beside it: an unrestored dependency is fixed
    /// by running something, and an unapproved one is fixed by somebody
    /// deciding.
    /// </remarks>
    private static Finding Describe(DependencyRequirement dependency) => new()
    {
        Message = $"'{dependency.Id}' is not on the approved list.",
        Expected = "every declared dependency approved by the production",
        Actual = dependency.Version is { Length: > 0 } version
            ? $"'{dependency.Id}' at '{version}'"
            : $"'{dependency.Id}', with no version declared",
        Remediation =
            $"Ask whoever maintains the approved list to add '{dependency.Id}', or replace it with " +
            "a dependency already on the list.",
    };
}
