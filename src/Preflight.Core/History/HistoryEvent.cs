namespace Preflight.Core.History;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core.Execution;

/// <summary>
/// One event read back out of the NDJSON history.
/// </summary>
/// <remarks>
/// A closed hierarchy — abstract, private constructor, two nested sealed
/// records — for the same reason <c>PolicyNode</c> and
/// <c>GraphValidationError</c> are: there are exactly two event types, and a
/// third one arriving from outside this assembly would be a shape no reader
/// knows how to count.
/// </remarks>
public abstract record HistoryEvent
{
    private HistoryEvent()
    {
    }

    /// <summary>When it started.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>How long it took.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>A validation run.</summary>
    public sealed record Run : HistoryEvent
    {
        /// <summary>
        /// Which stage ran.
        /// </summary>
        /// <remarks>
        /// The report prints "Blocking verdicts at BuildReadiness", and the
        /// stage is not decoration in that line: an upper bound of build time
        /// not spent can only be built out of blocks at build readiness. A
        /// pre-submit block saves a review, not a build.
        /// </remarks>
        public required ValidationStage Stage { get; init; }

        /// <summary>The aggregated verdict of the run.</summary>
        public required RunVerdict Verdict { get; init; }

        /// <summary>The run was cancelled, so its duration is not a duration.</summary>
        public required bool Partial { get; init; }

        /// <summary><c>--fail-on-warning</c> was in effect.</summary>
        public required bool FailOnWarning { get; init; }

        /// <summary><c>--no-skip</c> was in effect.</summary>
        public required bool NoSkip { get; init; }

        /// <summary>How many rules actually ran.</summary>
        public required int ExecutedCount { get; init; }

        /// <summary>What each rule did, when the record was not truncated.</summary>
        public required IReadOnlyList<HistoryExecution> Executions { get; init; }
    }

    /// <summary>A child process <c>preflight measure</c> timed.</summary>
    public sealed record External : HistoryEvent
    {
        /// <summary>The <c>--label</c> it was given.</summary>
        public required string Label { get; init; }

        /// <summary>What it returned.</summary>
        public required int ExitCode { get; init; }
    }
}
