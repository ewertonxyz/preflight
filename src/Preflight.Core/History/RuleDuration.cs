namespace Preflight.Core.History;

/// <summary>One rule's median duration.</summary>
/// <param name="RuleId">The rule.</param>
/// <param name="P50">Its median.</param>
public sealed record RuleDuration(string RuleId, TimeSpan P50);
