namespace Preflight.Core.History;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Everything <c>preflight report</c> prints, computed and not yet formatted.
/// </summary>
/// <remarks>
/// Data first, text second. <c>report</c> originally had no
/// <c>--format json</c> today, and the surest way to make one impossible later
/// is to build the screen directly and leave the numbers unreachable except by
/// re-parsing it.
/// </remarks>
public sealed record HistoryReport
{
    /// <summary>The window that was asked for.</summary>
    public required TimeSpan Window { get; init; }

    /// <summary>Runs inside the window.</summary>
    public required int RunCount { get; init; }

    /// <summary><c>Passed</c>.</summary>
    public required int PassedCount { get; init; }

    /// <summary><c>PassedWithWarnings</c>.</summary>
    public required int PassedWithWarningsCount { get; init; }

    /// <summary><c>Blocked</c>.</summary>
    public required int BlockedCount { get; init; }

    /// <summary>
    /// <c>Errored</c>.
    /// </summary>
    /// <remarks>
    /// A line of its own, so the breakdown adds up to <see cref="RunCount"/>. A
    /// breakdown that does not close loses the reader at the first time they
    /// add the column up.
    /// </remarks>
    public required int ErroredCount { get; init; }

    /// <summary>
    /// Runs blocked on their own merits, by stage.
    /// </summary>
    /// <remarks>
    /// A run promoted to <c>Blocked</c> by <c>--fail-on-warning</c> is not
    /// counted here. Confusing the two overstates what the tool caught, and the
    /// whole history exists in order not to make that mistake.
    /// </remarks>
    public required IReadOnlyList<StageBlockCount> BlockingVerdicts { get; init; }

    /// <summary>
    /// Runs that ended <c>Blocked</c> only because warnings were promoted.
    /// </summary>
    /// <remarks>
    /// Reported separately rather than folded in or dropped. The record carries
    /// the verdict and the flag but not which rule produced the block, so
    /// merging these into <see cref="BlockingVerdicts"/> would claim a
    /// distinction the history cannot support.
    /// </remarks>
    public required int PromotedBlockCount { get; init; }

    /// <summary>
    /// Runs made with <c>--no-skip</c>.
    /// </summary>
    /// <remarks>
    /// The flag is recorded on every run for this line. A contrast run reports
    /// more failures by design, and a thirty-day report that cannot tell it
    /// apart inflates the failure count.
    /// </remarks>
    public required int ContrastRunCount { get; init; }

    /// <summary>
    /// Runs that were cancelled, and therefore left out of the percentiles.
    /// </summary>
    public required int PartialRunCount { get; init; }

    /// <summary>Lines that could not be read.</summary>
    public required int UnreadableLineCount { get; init; }

    /// <summary>Well-formed lines naming an event type this version does not know.</summary>
    public required int IgnoredLineCount { get; init; }

    /// <summary>How long preflight itself took.</summary>
    public required DurationSummary PreflightDuration { get; init; }

    /// <summary>How long each measured label took, by label.</summary>
    public required IReadOnlyList<MeasuredSeries> Measured { get; init; }

    /// <summary>The slowest rules by median, longest first.</summary>
    public required IReadOnlyList<RuleDuration> SlowestRules { get; init; }

    /// <summary>How many more rules had a median that is not shown.</summary>
    public required int SlowestRulesNotShown { get; init; }

    /// <summary>The rules that failed most often.</summary>
    public required IReadOnlyList<RuleFailureCount> MostFrequentFailures { get; init; }

    /// <summary>How many more rules failed and are not shown.</summary>
    public required int MostFrequentFailuresNotShown { get; init; }

    /// <summary>
    /// Build time that a blocking verdict at build readiness might have saved.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> when there is no median build duration to
    /// multiply by. The assumption paragraph is part of the design rather than
    /// a footnote, and a ceiling computed from a number that does not exist is
    /// the fiction the paragraph exists to prevent.
    /// </remarks>
    public required TimeSpan? UpperBoundNotSpent { get; init; }
}
