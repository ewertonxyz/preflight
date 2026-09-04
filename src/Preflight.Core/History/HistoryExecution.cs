namespace Preflight.Core.History;

using Preflight.Abstractions.Model;

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
