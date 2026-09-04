namespace Preflight.Core.Tests.History;

using System.Text.Json;
using Preflight.Core.History;
using Preflight.TestSupport;

/// <summary>
/// Fixes the JSON projection of a history report, for
/// <c>report --format json</c>.
/// </summary>
/// <remarks>
/// Here rather than in <c>Cli.Tests</c> because the document lives in
/// <c>Preflight.Core</c>: it is data, not rendering, and the project layering keeps
/// output formatting out of the engine. The renderer that writes it lives in the
/// CLI, and the architecture guard depends on that separation holding.
/// </remarks>
public sealed class HistoryReportDocumentTests
{
    private static string Serialise(HistoryReport report) =>
        JsonSerializer.Serialize(
            HistoryReportDocument.For(report, report.Window),
            RunEventDocument.Indented);

    /// <summary>
    /// The two JSON documents this tool emits agree on casing and on enums.
    /// </summary>
    /// <remarks>
    /// Reusing <c>RunEventDocument</c>'s options rather than declaring a second
    /// set is the point of the decision: an enum written as its ordinal forces
    /// every consumer to keep a copy of the declaration order, and two option
    /// objects drift the first time one of them is edited.
    /// </remarks>
    [Fact]
    public void For_SerialisedWithTheRunEventOptions_WritesCamelCaseAndEnumNames()
    {
        var json = Serialise(HistoryReportFixture.DocumentedExample());

        json.ShouldContain("\"runCount\"");
        json.ShouldContain("\"stage\": \"BuildReadiness\"");

        // Case.Sensitive, and not by preference: Shouldly compares case
        // insensitively by default, so the PascalCase name this is looking for
        // would match the camelCase one two lines above and the assertion could
        // never hold — for a document that was already correct.
        json.ShouldNotContain("\"Stage\"", Case.Sensitive);
    }

    /// <summary>
    /// A percentile the sample cannot support is absent, never zero.
    /// </summary>
    /// <remarks>
    /// The honesty about an absent number, and it matters more here than on the
    /// screen: zero is a claim, "I did not measure" is not zero, and a machine
    /// consumer sums the zero without ever reading it. The canonical example
    /// already carries the interesting case — the build series has enough
    /// observations for a p50 and not for a p95.
    /// </remarks>
    [Fact]
    public void For_WithAP95TheSampleCannotSupport_OmitsItRatherThanWritingZero()
    {
        var json = Serialise(HistoryReportFixture.DocumentedExample());

        var measured = JsonDocument.Parse(json).RootElement.GetProperty("measured")[0];

        measured.GetProperty("duration").TryGetProperty("p95Ms", out _).ShouldBeFalse();
        measured.GetProperty("duration").GetProperty("p50Ms").GetInt64().ShouldBe(2282000);
        json.ShouldNotContain("\"p95Ms\": 0");
    }

    /// <remarks>
    /// Milliseconds, as a number, matching the <c>durationMs</c> the run event
    /// already writes. A raw <c>TimeSpan</c> serialises as
    /// <c>"00:00:00.4000000"</c> and would disagree with the JSON this same tool
    /// emits three lines earlier in the same pipeline.
    /// </remarks>
    [Fact]
    public void For_WritesEveryDurationAsMilliseconds()
    {
        var json = Serialise(HistoryReportFixture.DocumentedExample());
        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("preflightDuration").GetProperty("p50Ms").GetInt64().ShouldBe(18400);

        DurationValues(root).ShouldAllBe(value => value.ValueKind == JsonValueKind.Number);
    }

    /// <summary>
    /// Every duration in the document, wherever it appears.
    /// </summary>
    private static IReadOnlyList<JsonElement> DurationValues(JsonElement element) =>
    [
        .. element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().SelectMany(property =>
                property.Name.EndsWith("Ms", StringComparison.Ordinal)
                    ? [property.Value]
                    : DurationValues(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().SelectMany(DurationValues),
            _ => [],
        },
    ];

    /// <remarks>
    /// a breakdown that does not add up to the total loses the reader
    /// at the first time they add the column up, and <c>Errored</c> is the line
    /// the report's example omits.
    /// </remarks>
    [Fact]
    public void For_WritesErroredAsItsOwnCountSoTheBreakdownClosesWithTheTotal()
    {
        var json = Serialise(HistoryReportFixture.DocumentedExample() with
        {
            ErroredCount = 3,
            RunCount = 145,
        });

        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("erroredCount").GetInt32().ShouldBe(3);

        var breakdown = root.GetProperty("passedCount").GetInt32() +
            root.GetProperty("passedWithWarningsCount").GetInt32() +
            root.GetProperty("blockedCount").GetInt32() +
            root.GetProperty("erroredCount").GetInt32();

        breakdown.ShouldBe(root.GetProperty("runCount").GetInt32());
    }

    /// <remarks>
    /// Publishing percentiles over an unknown fraction of the sample is a
    /// measurement dressed as a fact. The counts are how a machine
    /// consumer discovers the fraction it did not get.
    /// </remarks>
    [Fact]
    public void For_WritesTheUnreadableAndIgnoredLineCounts()
    {
        var json = Serialise(HistoryReportFixture.DocumentedExample() with
        {
            UnreadableLineCount = 3,
            IgnoredLineCount = 1,
        });

        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("unreadableLineCount").GetInt32().ShouldBe(3);
        root.GetProperty("ignoredLineCount").GetInt32().ShouldBe(1);
    }

    /// <remarks>
    /// A cancelled run counts towards the verdicts and stays out of the
    /// percentiles; a <c>--no-skip</c> run reports more failures because that is
    /// what it is for.
    /// The flag is recorded on every run so that a report can say it was in
    /// force, and a reader who cannot see it cannot explain the number.
    /// </remarks>
    [Fact]
    public void For_WritesThePartialRunCountAndTheContrastRunCount()
    {
        var json = Serialise(HistoryReportFixture.DocumentedExample() with
        {
            PartialRunCount = 2,
            ContrastRunCount = 9,
        });

        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("partialRunCount").GetInt32().ShouldBe(2);
        root.GetProperty("contrastRunCount").GetInt32().ShouldBe(9);
    }
}
