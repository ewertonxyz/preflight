namespace Preflight.Core.History;

/// <summary>
/// The JSON shape of a history report, for <c>report --format json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="RunEventDocument"/> deliberately, down to reusing its
/// serializer options, so that the two JSON documents this tool emits agree on
/// casing, on enum rendering and on how a duration is spelled. Serialising a
/// <see cref="TimeSpan"/> raw would write <c>"00:00:00.4000000"</c> and
/// disagree with the <c>durationMs</c> the run event writes three lines earlier
/// in the same pipeline.
/// </para>
/// <para>
/// The contract for the absences matters more here than on the screen: a
/// percentile the sample does not support is missing from the document, never
/// present as zero. Zero is a claim, and a machine consumer sums it without
/// ever reading it.
/// </para>
/// <para>
/// Deliberately without the <c>type</c> discriminator the run event carries. A
/// report is not a history event, and a document that looked like one would
/// invite somebody to append it to the NDJSON.
/// </para>
/// </remarks>
public static class HistoryReportDocument
{
    /// <summary>
    /// The whole report, as an object the serializer writes.
    /// </summary>
    /// <param name="report">What <c>report</c> computed.</param>
    /// <param name="window">The window it was asked for.</param>
    public static object For(HistoryReport report, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new
        {
            windowMs = Milliseconds(window),
            runCount = report.RunCount,
            passedCount = report.PassedCount,
            passedWithWarningsCount = report.PassedWithWarningsCount,
            blockedCount = report.BlockedCount,

            // A line of its own so the breakdown adds up to runCount. Section
            // A breakdown that does not close loses the reader at the first
            // time they add the column up.
            erroredCount = report.ErroredCount,

            promotedBlockCount = report.PromotedBlockCount,

            // A contrast run reports more failures by design, and a
            // cancelled one contributes a verdict but no percentile. A consumer
            // that cannot see either number is computing a rate over a sample it
            // does not know the shape of.
            contrastRunCount = report.ContrastRunCount,
            partialRunCount = report.PartialRunCount,

            unreadableLineCount = report.UnreadableLineCount,
            ignoredLineCount = report.IgnoredLineCount,
            blockingVerdicts = report.BlockingVerdicts.Select(entry => new
            {
                stage = entry.Stage,
                count = entry.Count,
            }),
            preflightDuration = Describe(report.PreflightDuration),
            measured = report.Measured.Select(series => new
            {
                label = series.Label,
                duration = Describe(series.Duration),
            }),
            slowestRules = report.SlowestRules.Select(rule => new
            {
                ruleId = rule.RuleId,
                p50Ms = Milliseconds(rule.P50),
            }),
            slowestRulesNotShown = report.SlowestRulesNotShown,
            mostFrequentFailures = report.MostFrequentFailures.Select(failure => new
            {
                ruleId = failure.RuleId,
                count = failure.Count,
            }),
            mostFrequentFailuresNotShown = report.MostFrequentFailuresNotShown,

            // Absent when there is no median build to multiply by. The report
            // calls the assumption paragraph part of the design rather than a
            // footnote, and a ceiling computed from a number that does not exist
            // is the fiction that paragraph prevents.
            upperBoundNotSpentMs = Milliseconds(report.UpperBoundNotSpent),
        };
    }

    /// <remarks>
    /// The sample size travels with the numbers, as it does on the screen, so
    /// that a percentile cannot be quoted without the <c>n</c> it came from.
    /// </remarks>
    private static object Describe(DurationSummary summary) => new
    {
        sampleSize = summary.SampleSize,
        p50Ms = Milliseconds(summary.P50),
        p95Ms = Milliseconds(summary.P95),
    };

    private static long Milliseconds(TimeSpan value) => (long)value.TotalMilliseconds;

    /// <remarks>
    /// Nullable all the way to the serializer, which is configured to omit a
    /// null rather than write one. Coalescing to zero here would be the exact
    /// false green this exists to refuse, one line before the option that
    /// prevents it.
    /// </remarks>
    private static long? Milliseconds(TimeSpan? value) =>
        value is { } present ? Milliseconds(present) : null;
}
