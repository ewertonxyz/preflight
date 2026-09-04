namespace Preflight.Core.History;

/// <summary>How often one rule failed.</summary>
/// <param name="RuleId">The rule.</param>
/// <param name="Count">How many executions ended <c>Failed</c> or <c>Errored</c>.</param>
public sealed record RuleFailureCount(string RuleId, int Count);
