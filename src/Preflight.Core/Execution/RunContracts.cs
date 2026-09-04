namespace Preflight.Core.Execution;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Why a rule was skipped.
/// </summary>
/// <remarks>
/// Lives here rather than in <c>Preflight.Abstractions</c> because no rule ever
/// produces one — only the engine does, and only the Abstractions surface is
/// versioned as a plugin contract.
/// </remarks>
public enum SkipReason
{
    DependencyFailed,
    DependencyErrored,
    DependencyDisabled,
}

/// <summary>
/// The outcome of a whole run.
/// </summary>
public enum RunVerdict
{
    Passed,
    PassedWithWarnings,
    Blocked,
    Errored,
}

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

/// <summary>
/// One complete run.
/// </summary>
/// <remarks>
/// <see cref="PolicyChain"/> holds the files that composed the
/// policy, in order, which is what makes a line of history auditable months
/// later.
/// </remarks>
public sealed record RunResult
{
    public required Guid RunId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required TimeSpan Duration { get; init; }

    public required ValidationStage Stage { get; init; }

    public required BuildTarget Target { get; init; }

    public required string? Pipeline { get; init; }

    /// <summary>
    /// The version of the installed pipeline package this run resolved to, or
    /// <see langword="null"/> when no package took part.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>init</c> and not <c>required</c>, so that every construction site that
    /// predates packages keeps compiling and keeps meaning what it meant. The
    /// default has to be <see langword="null"/> for the same reason it is not
    /// required: a site nobody updated would otherwise claim a package that does
    /// not exist, which is a false green aimed at the provenance itself.
    /// </para>
    /// <para>
    /// The version travels here and <c>PipelineVersionSource</c> does not,
    /// because they answer different audiences. Which version ran is what lets a
    /// machine reader tell two runs of one commit apart, so it belongs in the
    /// NDJSON and in <c>--format json</c>; <em>why</em> that version was chosen
    /// is an explanation for the person reading the header, and it reaches the
    /// reporter beside the result, exactly as the pipeline's own selection
    /// source already does. See ADR-034.
    /// </para>
    /// </remarks>
    public string? PipelineVersion { get; init; }

    public required IReadOnlyList<string> PolicyChain { get; init; }

    public required IReadOnlyList<RuleExecution> Executions { get; init; }

    public required RunVerdict Verdict { get; init; }

    public required bool Partial { get; init; }

    public required bool FailOnWarning { get; init; }

    /// <summary>
    /// <c>--no-skip</c> was in effect: gating propagation was suppressed and
    /// rules that would have been skipped executed instead.
    /// </summary>
    /// <remarks>
    /// Recorded for the same reason <see cref="FailOnWarning"/> is. A contrast
    /// run reports more failures than a normal one by design, and a
    /// <c>report</c> over thirty days of history that cannot tell the two apart
    /// inflates the failure count — the metric that overstates the tool, which
    /// is the error the whole history exists to avoid making.
    ///
    /// It also changes what the console header has to say: <c>--no-skip</c> can
    /// turn a green run red, and a reader who cannot see the flag cannot
    /// explain why.
    /// </remarks>
    public required bool NoSkip { get; init; }
}
