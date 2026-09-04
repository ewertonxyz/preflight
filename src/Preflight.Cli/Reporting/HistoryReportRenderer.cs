namespace Preflight.Cli.Reporting;

using System.Globalization;
using System.Text;
using Preflight.Cli.Parsing;
using Preflight.Core.History;

/// <summary>
/// Renders the <c>report</c> screen.
/// </summary>
/// <remarks>
/// <para>
/// Formatting only. Every number here was decided by
/// <see cref="HistoryReportBuilder"/>, including which ones do not exist — a
/// renderer that computed a fallback for a suppressed percentile would undo the
/// decision that gives this screen its point.
/// </para>
/// <para>
/// The three caveat lines under the upper bound are printed in full, every
/// time. That paragraph is part of the design rather than a footnote: a report
/// that says "9h30m saved" without them is fiction wearing a metric's clothes,
/// and the difference between an engineering tool and a slide is exactly those
/// three lines.
/// </para>
/// </remarks>
public sealed class HistoryReportRenderer
{
    private const int CountLabelWidth = 34;
    private const int CountWidth = 3;
    private const int SeriesLabelWidth = 23;
    private const int PercentileWidth = 8;
    private const int BlockLabelWidth = 47;
    private const int RuleLabelWidth = 32;
    private const int FailureCountWidth = 3;

    // The duration column of "Slowest rules" is right-aligned, so the label is
    // one narrower than the one above and the number carries the difference.
    // A right-aligned column is what makes
    // 14.9s and 2.1s comparable at a glance.
    private const int SlowRuleLabelWidth = 31;
    private const int SlowRuleDurationWidth = 6;

    private readonly ConsoleCapabilities _capabilities;
    private readonly GlyphSet _glyphs;

    public HistoryReportRenderer(ConsoleCapabilities capabilities, GlyphSet glyphs)
    {
        _capabilities = capabilities;
        _glyphs = glyphs;
    }

    /// <summary>
    /// Writes the whole report.
    /// </summary>
    public void Report(HistoryReport report, SinceWindow window)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(window);

        var writer = new StringBuilder();

        writer.Append("Preflight history ").Append(_glyphs.Separator)
            .Append(" last ").Append(window.Describe()).Append('\n')
            .Append('\n');

        if (report.RunCount == 0 && report.Measured.Count == 0)
        {
            // Nothing recorded must not read as a row of measured zeros. An
            // empty history is a valid answer.
            writer.Append("Nothing recorded in this window.\n");
            WriteNotes(writer, report);
            _capabilities.Output.Write(writer.ToString());

            return;
        }

        WriteCounts(writer, report);
        writer.Append('\n');
        WriteDurations(writer, report);
        WriteBlocking(writer, report);
        WriteNotes(writer, report);
        WriteSlowestRules(writer, report);
        WriteMostFrequentFailures(writer, report);

        _capabilities.Output.Write(writer.ToString());
    }

    /// <remarks>
    /// The total is printed even when it is zero, and the breakdown under it is
    /// not. A history holding only measurements is a real state — somebody
    /// timed a build before validating anything — and it has to read as nought
    /// runs rather than as a section that quietly disappeared.
    /// </remarks>
    private static void WriteCounts(StringBuilder writer, HistoryReport report)
    {
        writer.Append("Runs".PadRight(CountLabelWidth))
            .Append(Number(report.RunCount).PadLeft(CountWidth))
            .Append('\n');

        Count(writer, "  Passed", report.PassedCount);
        Count(writer, "  Passed with warnings", report.PassedWithWarningsCount);
        Count(writer, "  Blocked", report.BlockedCount);
        Count(writer, "  Errored", report.ErroredCount);
    }

    /// <remarks>
    /// A zero row is not printed. The four rows below the total cover every
    /// verdict the aggregation can produce, so what is shown always adds up to
    /// what is above it, and a row of zeroes does not add to it.
    /// </remarks>
    private static void Count(StringBuilder writer, string label, int count)
    {
        if (count == 0)
        {
            return;
        }

        writer.Append(label.PadRight(CountLabelWidth))
            .Append(Number(count).PadLeft(CountWidth))
            .Append('\n');
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <remarks>
    /// A label is capitalised for the header and otherwise left alone. The
    /// guard is for a label that came out of a history file rather than out of
    /// <c>--label</c>, which the parser will not accept empty.
    /// </remarks>
    private static string Describe(string label) =>
        label.Length == 0 ? label : char.ToUpperInvariant(label[0]) + label[1..];

    private void WriteDurations(StringBuilder writer, HistoryReport report)
    {
        WriteSeries(writer, "Preflight duration", report.PreflightDuration, measured: false);

        foreach (var series in report.Measured)
        {
            WriteSeries(writer, Describe(series.Label) + " duration", series.Duration, measured: true);
        }
    }

    private void WriteSeries(StringBuilder writer, string label, DurationSummary duration, bool measured)
    {
        writer.Append(label.PadRight(SeriesLabelWidth))
            .Append("p50  ").Append(Percentile(duration.P50).PadRight(PercentileWidth))
            .Append("p95  ").Append(Percentile(duration.P95).PadRight(PercentileWidth))
            .Append(Sample(duration, measured))
            .Append('\n');
    }

    private string Percentile(TimeSpan? value) =>
        value is { } duration ? DurationFormat.Scaled(duration) : _glyphs.Absent;

    /// <remarks>
    /// The sample size is never printed on its own: whenever a percentile is
    /// missing, the reason is printed with it. A bare dash satisfies the letter
    /// of the rule and not the requirement — the reader has to be told what
    /// would make the number exist.
    /// </remarks>
    private static string Sample(DurationSummary duration, bool measured)
    {
        var writer = new StringBuilder("(n=").Append(Number(duration.SampleSize));

        if (measured)
        {
            writer.Append(", measured");
        }

        if (duration.P50 is null)
        {
            writer.Append("; p50 needs n>=").Append(Number(PercentileCalculator.MinimumSampleForP50));
        }
        else if (duration.P95 is null)
        {
            writer.Append("; p95 needs n>=").Append(Number(PercentileCalculator.MinimumSampleForP95));
        }

        return writer.Append(')').ToString();
    }

    private void WriteBlocking(StringBuilder writer, HistoryReport report)
    {
        if (report.BlockingVerdicts.Count == 0)
        {
            return;
        }

        writer.Append('\n');

        foreach (var stage in report.BlockingVerdicts)
        {
            writer.Append(("Blocking verdicts at " + stage.Stage).PadRight(BlockLabelWidth))
                .Append(Number(stage.Count))
                .Append('\n');
        }

        WriteUpperBound(writer, report);
    }

    private void WriteUpperBound(StringBuilder writer, HistoryReport report)
    {
        var blocks = report.BlockingVerdicts
            .Where(stage => stage.Stage == Preflight.Abstractions.Model.ValidationStage.BuildReadiness)
            .Sum(stage => stage.Count);

        if (report.UpperBoundNotSpent is not { } bound)
        {
            // The block is omitted rather than filled with a zero or
            // with an arithmetic expression containing a dash. Zero is a claim,
            // and "I did not measure" is not zero.
            writer.Append("  Upper bound of build time not spent ").Append(_glyphs.Separator)
                .Append(" not computed, for want of a p50 '")
                .Append(HistoryReportBuilder.BuildLabel)
                .Append("' duration.\n")
                .Append("  Record one with 'preflight measure --label ")
                .Append(HistoryReportBuilder.BuildLabel)
                .Append(" -- <build command>'.\n");

            return;
        }

        writer.Append("Upper bound of build time not spent".PadRight(BlockLabelWidth))
            .Append(DurationFormat.Scaled(bound))
            .Append('\n')
            .Append("  = ").Append(Number(blocks)).Append(" blocking verdicts x p50 ")
            .Append(HistoryReportBuilder.BuildLabel).Append(" duration\n")
            .Append("  Assumes every blocked run would otherwise have failed the build.\n")
            .Append("  Not all would. Treat as a ceiling, not a saving.\n");
    }

    /// <remarks>
    /// Every line here exists because leaving it out would let a number be read
    /// as something it is not: a contrast run reports more failures by design,
    /// a cancelled run has no duration worth averaging, and a line nobody could
    /// read is a hole in the sample the reader is entitled to know the size of.
    /// </remarks>
    private static void WriteNotes(StringBuilder writer, HistoryReport report)
    {
        Note(
            writer,
            report.PromotedBlockCount,
            "run blocked only by --fail-on-warning, not counted as a blocking verdict",
            "runs blocked only by --fail-on-warning, not counted as blocking verdicts");
        Note(
            writer,
            report.ContrastRunCount,
            "run made with --no-skip, which reports more failures by design",
            "runs made with --no-skip, which report more failures by design");
        Note(
            writer,
            report.PartialRunCount,
            "run cancelled, and left out of the duration percentiles",
            "runs cancelled, and left out of the duration percentiles");
        Note(
            writer,
            report.UnreadableLineCount,
            "history line could not be read and was skipped",
            "history lines could not be read and were skipped");
        Note(
            writer,
            report.IgnoredLineCount,
            "history line named an event type this version does not know",
            "history lines named an event type this version does not know");
    }

    /// <remarks>
    /// Both forms are written out rather than assembled from a noun and an "s".
    /// The verb has to agree too — "3 history lines was skipped" is what
    /// assembling produces — and these lines are read by somebody deciding
    /// whether to trust the numbers above them, which is not the moment to
    /// spend credibility on grammar.
    /// </remarks>
    private static void Note(StringBuilder writer, int count, string singular, string plural)
    {
        if (count == 0)
        {
            return;
        }

        writer.Append("  ").Append(Number(count)).Append(' ')
            .Append(count == 1 ? singular : plural)
            .Append('\n');
    }

    private static void WriteSlowestRules(StringBuilder writer, HistoryReport report)
    {
        if (report.SlowestRules.Count == 0)
        {
            return;
        }

        writer.Append('\n').Append("Slowest rules (p50)\n");

        foreach (var rule in report.SlowestRules)
        {
            writer.Append("  ").Append(rule.RuleId.PadRight(SlowRuleLabelWidth))
                .Append(DurationFormat.Scaled(rule.P50).PadLeft(SlowRuleDurationWidth))
                .Append('\n');
        }

        More(writer, report.SlowestRulesNotShown);
    }

    private static void WriteMostFrequentFailures(StringBuilder writer, HistoryReport report)
    {
        if (report.MostFrequentFailures.Count == 0)
        {
            return;
        }

        writer.Append('\n').Append("Most frequent failures\n");

        foreach (var rule in report.MostFrequentFailures)
        {
            writer.Append("  ").Append(rule.RuleId.PadRight(RuleLabelWidth))
                .Append(Number(rule.Count).PadLeft(FailureCountWidth))
                .Append('\n');
        }

        More(writer, report.MostFrequentFailuresNotShown);
    }

    /// <remarks>
    /// A ranking that silently stops at five reads as "these are all of them".
    /// Saying how many were dropped costs one line and is the difference
    /// between a top five and a wrong list.
    /// </remarks>
    private static void More(StringBuilder writer, int notShown)
    {
        if (notShown == 0)
        {
            return;
        }

        writer.Append("  ").Append(Number(notShown)).Append(" more not shown\n");
    }
}
