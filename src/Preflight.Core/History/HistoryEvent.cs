namespace Preflight.Core.History;

using Preflight.Abstractions;

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

/// <summary>
/// One rule's part of a recorded run.
/// </summary>
/// <param name="RuleId">The rule.</param>
/// <param name="Status">What it did.</param>
/// <param name="Duration">How long it took.</param>
/// <param name="FromCache">
/// The result was served from the incremental cache, so the duration is a
/// lookup rather than a run.
/// </param>
/// <remarks>
/// <c>FromCache</c> is read back for one reason: without it, the report's
/// "slowest rules" is computed over a series in which every cache hit
/// contributes nought seconds, and the ranking collapses towards whichever rule
/// caches best. That is the same mistake a skipped execution would cause if it
/// were counted, and here it is worse — it would destroy the very measurement
/// that says whether the cache was worth building.
/// </remarks>
public sealed record HistoryExecution(
    string RuleId,
    RuleStatus Status,
    TimeSpan Duration,
    bool FromCache = false);

/// <summary>
/// One line of the history, whether or not it could be understood.
/// </summary>
/// <remarks>
/// The unreadable and ignored shapes are the whole reason this type exists
/// rather than a bare <see cref="HistoryEvent"/>. The format spends four
/// paragraphs establishing that a network share can produce an interleaved
/// line, and a reader that silently swallowed one would let the report publish
/// percentiles over an unknown fraction of the sample — principle 7, pointed at
/// the instrumentation itself.
/// </remarks>
public abstract record HistoryEntry
{
    private HistoryEntry()
    {
    }

    /// <summary>A line that was understood.</summary>
    /// <param name="Value">The event it carried.</param>
    public sealed record Parsed(HistoryEvent Value) : HistoryEntry;

    /// <summary>A line that could not be understood, and is counted as such.</summary>
    /// <param name="File">The file it was in.</param>
    /// <param name="Line">Its one-based position in that file.</param>
    public sealed record Unreadable(string File, int Line) : HistoryEntry;

    /// <summary>
    /// A well-formed line naming an event type this version does not know.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Unreadable"/> because it means something
    /// different: not damage, but a newer or a future writer. It is what lets a
    /// later phase add an event type without invalidating the history already
    /// on disk.
    /// </remarks>
    /// <param name="Type">The <c>type</c> it declared.</param>
    public sealed record Ignored(string Type) : HistoryEntry;
}
