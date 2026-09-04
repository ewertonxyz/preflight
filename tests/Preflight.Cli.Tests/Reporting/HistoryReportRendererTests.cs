namespace Preflight.Cli.Tests.Reporting;

using System.Text;
using Preflight.Abstractions.Rules;
using Preflight.Cli.Parsing;
using Preflight.Cli.Reporting;
using Preflight.Core.History;
using Preflight.TestSupport;

/// <summary>
/// Fixes the rendered report.
/// </summary>
/// <remarks>
/// Golden files, for the same reason the console report's report has them: the
/// correctness of this screen is alignment, ordering, and which caveats appear
/// next to which number. A containment assertion is invariant under every one
/// of those defects, and this is the screen somebody puts in front of other
/// people.
/// </remarks>
public sealed class HistoryReportRendererTests
{
    /// <summary>
    /// The whole screen the report draws.
    /// </summary>
    [Fact]
    public Task Report_ForTheDocumentedExample_MatchesTheGolden() =>
        Verify(Render(HistoryReportFixture.DocumentedExample()));

    /// <summary>
    /// A suppressed percentile prints a dash and the reason for it.
    /// </summary>
    /// <remarks>
    /// The documented example already contains the interesting case, and it is
    /// interesting because both halves appear on one line: the build series has
    /// enough observations for a p50 and not for a p95. A gate applied to the
    /// whole series rather than to each percentile would pass a test with one
    /// series and be wrong on the example the design document draws.
    /// </remarks>
    [Fact]
    public void Report_WithASeriesBelowTheP95Minimum_PrintsTheDashAndWhatIsMissing()
    {
        var text = Render(HistoryReportFixture.DocumentedExample());

        text.ShouldContain("p50  38m02s");
        text.ShouldContain("(n=27, measured; p95 needs n>=50)");
    }

    /// <summary>
    /// Without a median build there is no ceiling, and the block says so.
    /// </summary>
    /// <remarks>
    /// The block is omitted rather than filled with a zero or with an
    /// arithmetic expression containing a dash. Zero is a claim; "I did not
    /// measure" is not zero.
    /// </remarks>
    [Fact]
    public Task Report_WithoutAMeasuredBuild_OmitsTheUpperBoundAndSaysWhat_IsMissing() =>
        Verify(Render(HistoryReportFixture.DocumentedExample() with
        {
            Measured = [],
            UpperBoundNotSpent = null,
        }));

    /// <remarks>
    /// Applied to this command: nothing recorded must not read as a row
    /// of measured zeroes. The exit-code contract already calls an empty history a valid
    /// answer rather than an error.
    /// </remarks>
    [Fact]
    public Task Report_OverAnEmptyHistory_SaysSoRatherThanPrintingZeroes() =>
        Verify(Render(HistoryReportFixture.Empty()));

    /// <summary>
    /// Every caveat about an absent number, on one screen.
    /// </summary>
    [Fact]
    public Task Report_WithEverythingWorthACaveat_PrintsAllOfThem() =>
        Verify(Render(HistoryReportFixture.DocumentedExample() with
        {
            ErroredCount = 3,
            RunCount = 145,
            PromotedBlockCount = 4,
            ContrastRunCount = 9,
            PartialRunCount = 2,
            UnreadableLineCount = 3,
            IgnoredLineCount = 1,
            SlowestRulesNotShown = 4,
            MostFrequentFailuresNotShown = 2,
        }));

    /// <summary>
    /// The ASCII variant contains no character outside ASCII. Any of them.
    /// </summary>
    /// <remarks>
    /// The marker for a percentile the sample cannot support is an em dash, and
    /// several Windows build agents render one as a question
    /// mark. A report whose "not enough data" marker is unreadable has replaced
    /// a missing number with a wrong-looking one — so the marker belongs to the
    /// glyph set, like the separator and the arrow.
    /// </remarks>
    [Fact]
    public void Report_WithTheAsciiVariant_ContainsNothingOutsideAscii()
    {
        var text = Render(HistoryReportFixture.DocumentedExample(), GlyphSet.Ascii);

        text.ShouldAllBe(character => character < 128);
    }

    /// <summary>
    /// A measured label that is empty is printed as it came, not crashed on.
    /// </summary>
    /// <remarks>
    /// <c>--label</c> will not accept an empty string, so the only way to get
    /// one is a history file somebody wrote by hand or a writer from another
    /// version. The history format makes a damaged history a thing to report rather
    /// than a thing to fall over on, and that has to hold for the reporting too.
    /// </remarks>
    [Fact]
    public void Report_ForAMeasuredLabelThatIsEmpty_StillRenders()
    {
        var text = Render(HistoryReportFixture.DocumentedExample() with
        {
            Measured = [new MeasuredSeries(string.Empty, new DurationSummary(5, TimeSpan.FromSeconds(60), null))],
        });

        text.ShouldContain("p50  1m00s");
        text.ShouldContain("(n=5, measured; p95 needs n>=50)");
    }

    private static string Render(HistoryReport report, GlyphSet? glyphs = null)
    {
        var output = new StringWriter();

        new HistoryReportRenderer(
                new ConsoleCapabilities(
                    output,
                    Encoding.UTF8,
                    IsInteractive: false,
                    IsInputInteractive: false,
                    ConsoleCapabilities.DefaultWidth),
                glyphs ?? GlyphSet.Unicode)
            .Report(report, SinceDuration.Parse("30d")!);

        return output.ToString();
    }
}
