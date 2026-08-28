namespace Preflight.Abstractions;

/// <summary>
/// The result of running a rule once.
/// </summary>
/// <remarks>
/// There are deliberately no <c>Skipped()</c> nor <c>Errored()</c> factories:
/// those two statuses are produced by the engine — gating propagation and
/// exception/timeout isolation, respectively — not by a rule declaring itself
/// either one. See IDEAS.md for the gap between that intent and what
/// <see cref="Status"/>'s public <c>init</c> setter still allows.
/// </remarks>
public sealed record RuleOutcome
{
    public required RuleStatus Status { get; init; }

    public IReadOnlyList<Finding> Findings { get; init; } = [];

    public static RuleOutcome Passed() =>
        new() { Status = RuleStatus.Passed };

    public static RuleOutcome NotApplicable() =>
        new() { Status = RuleStatus.NotApplicable };

    public static RuleOutcome Warned(params Finding[] findings) =>
        new() { Status = RuleStatus.Warning, Findings = findings };

    public static RuleOutcome Failed(params Finding[] findings) =>
        new() { Status = RuleStatus.Failed, Findings = findings };
}
