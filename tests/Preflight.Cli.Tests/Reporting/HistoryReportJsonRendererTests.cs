namespace Preflight.Cli.Tests.Reporting;

using Preflight.Cli.Parsing;
using Preflight.Cli.Reporting;
using Preflight.TestSupport;

/// <summary>
/// Fixes the JSON form of the report.
/// </summary>
/// <remarks>
/// Exactly what <c>HistoryReportRendererTests</c> does for the screen, over the
/// same fixture, because the two are two renderings of one record and the whole
/// argument for building this cheaply was that the history kept them that way.
/// </remarks>
public sealed class HistoryReportJsonRendererTests
{
    private static string Render(Preflight.Core.History.HistoryReport report)
    {
        var output = new StringWriter();

        new HistoryReportJsonRenderer(output).Report(report, SinceDuration.Parse("30d")!);

        return output.ToString();
    }

    [Fact]
    public Task Report_ForTheCanonicalExample_MatchesTheGolden() =>
        Verify(Render(HistoryReportFixture.CanonicalExample()));

    /// <summary>
    /// An empty history is a document, not a row of measured zeros.
    /// </summary>
    /// <remarks>
    /// The empty-history case in the form a pipeline consumes it: a run count of nought with
    /// every percentile absent says "nothing was recorded", and the same
    /// document with zeros in those places would say "everything took no time".
    /// A machine cannot tell those apart after the fact, which is why the shape
    /// is pinned rather than asserted a field at a time.
    /// </remarks>
    [Fact]
    public Task Report_OverAnEmptyHistory_MatchesTheGolden() =>
        Verify(Render(HistoryReportFixture.Empty()));
}
