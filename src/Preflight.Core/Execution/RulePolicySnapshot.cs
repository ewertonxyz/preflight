namespace Preflight.Core.Execution;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core.Policy;

/// <summary>
/// The effective policy for one rule, frozen before it runs.
/// </summary>
/// <remarks>
/// These values are recorded on the execution rather than looked up while
/// reporting. Taking the snapshot as its own step is what makes that testable
/// without running anything, and it guarantees every part of the run —
/// including the skip propagation, which happens after the fact — sees one
/// consistent set of values.
/// </remarks>
public sealed record RulePolicySnapshot
{
    public required RuleId RuleId { get; init; }

    public required bool Enabled { get; init; }

    public required bool Blocking { get; init; }

    public required bool Gating { get; init; }

    public required Severity EffectiveSeverity { get; init; }

    public required TimeSpan Timeout { get; init; }

    public static RulePolicySnapshot For(RuleId ruleId, EffectivePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return new RulePolicySnapshot
        {
            RuleId = ruleId,
            Enabled = policy.RuleValue<bool>(ruleId, "enabled").Value,
            Blocking = policy.RuleValue<bool>(ruleId, "blocking").Value,
            Gating = policy.RuleValue<bool>(ruleId, "gating").Value,
            EffectiveSeverity = policy.RuleValue<Severity>(ruleId, "severity").Value,

            // Read, never re-derived: the cascade that fills a rule's missing
            // timeout from the root default has already run, and a second
            // derivation here would be a second answer to one question.
            Timeout = TimeSpan.FromSeconds(policy.RuleValue<long>(ruleId, "timeoutSeconds").Value),
        };
    }

    public static IReadOnlyDictionary<RuleId, RulePolicySnapshot> ForAll(
        IEnumerable<RuleId> ruleIds, EffectivePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(ruleIds);

        return ruleIds.ToDictionary(ruleId => ruleId, ruleId => For(ruleId, policy));
    }
}
