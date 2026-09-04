namespace Preflight.Core.Execution;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// What happened to one rule in one run.
/// </summary>
/// <remarks>
/// <see cref="EffectiveSeverity"/>, <see cref="Blocking"/> and
/// <see cref="Gating"/> are recorded rather than merely consulted: a report over
/// thirty days of history has to be able to answer "was this rule blocking when
/// it failed?" after the policy has since changed. Instrumentation that does
/// not record the policy in force produces numbers that look historical and are
/// not.
/// </remarks>
public sealed record RuleExecution
{
    public required RuleId RuleId { get; init; }

    public required RuleStatus Status { get; init; }

    public required Severity EffectiveSeverity { get; init; }

    public required bool Blocking { get; init; }

    public required bool Gating { get; init; }

    public required TimeSpan Duration { get; init; }

    public IReadOnlyList<Finding> Findings { get; init; } = [];

    public IReadOnlyList<RuleId> SkippedBecauseOf { get; init; } = [];

    public SkipReason? SkipReason { get; init; }

    public bool FromCache { get; init; }

    public string? ErrorDetail { get; init; }
}
