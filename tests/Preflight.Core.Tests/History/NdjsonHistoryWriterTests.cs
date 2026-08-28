namespace Preflight.Core.Tests.History;

using System.Text.Json;
using Preflight.Core.History;
using Preflight.Core.Policy;
using Preflight.TestSupport;

/// <summary>
/// Where a record goes, per the history format.
/// </summary>
public sealed class NdjsonHistoryWriterTests
{
    private static readonly DirectoryInfo Workspace =
        new(Path.Combine(Path.GetTempPath(), "preflight-writer"));

    private static readonly EngineEnvironment Machine = new()
    {
        ProcessorCount = 8,
        MachineName = "WKS-1234",
        ProcessId = 4242,
    };

    private readonly RecordingHistoryStore _store = new();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 8, 26, 14, 30, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(HistoryMode.Shared, "2026-08.WKS-1234.ndjson")]
    [InlineData(HistoryMode.PerProcess, "2026-08.WKS-1234.4242.ndjson")]
    public async Task WriteRunAsync_ForEachMode_WritesTheDocumentedFileName(HistoryMode mode, string expected)
    {
        await Writer().WriteRunAsync(
            Workspace,
            new HistorySettings(".preflight/history", mode),
            RunResultFixture.DocumentedExample(),
            TestContext.Current.CancellationToken);

        _store.Appended.ShouldHaveSingleItem().Path.ShouldBe(
            Path.Combine(Workspace.FullName, ".preflight/history", expected));
    }

    /// <remarks>
    /// A rooted <c>historyPath</c> is the network-share case of the history format,
    /// and it is the reason the path is not always under the workspace.
    /// </remarks>
    [Fact]
    public async Task WriteRunAsync_ForARootedHistoryPath_LeavesItWhereItWasPointed()
    {
        var share = Path.Combine(Path.GetTempPath(), "preflight-share");

        await Writer().WriteRunAsync(
            Workspace,
            new HistorySettings(share, HistoryMode.PerProcess),
            RunResultFixture.DocumentedExample(),
            TestContext.Current.CancellationToken);

        _store.Appended.ShouldHaveSingleItem().Path.ShouldBe(
            Path.Combine(share, "2026-08.WKS-1234.4242.ndjson"));
    }

    /// <remarks>
    /// The month comes from the injected clock, so a run written in September
    /// lands in September's file without the test waiting for September.
    /// </remarks>
    [Fact]
    public async Task WriteRunAsync_AfterTheClockCrossesAMonth_WritesToTheNewFile()
    {
        var settings = new HistorySettings(".preflight/history", HistoryMode.Shared);
        var writer = Writer();

        await writer.WriteRunAsync(
            Workspace, settings, RunResultFixture.DocumentedExample(), TestContext.Current.CancellationToken);

        _clock.Advance(TimeSpan.FromDays(7));

        await writer.WriteRunAsync(
            Workspace, settings, RunResultFixture.DocumentedExample(), TestContext.Current.CancellationToken);

        _store.Appended.Select(entry => Path.GetFileName(entry.Path))
            .ShouldBe(["2026-08.WKS-1234.ndjson", "2026-09.WKS-1234.ndjson"]);
    }

    [Fact]
    public async Task WriteRunAsync_WritesTheRunAsOneLine()
    {
        await Writer().WriteRunAsync(
            Workspace,
            new HistorySettings(".preflight/history", HistoryMode.Shared),
            RunResultFixture.DocumentedExample(),
            TestContext.Current.CancellationToken);

        var line = _store.Appended.ShouldHaveSingleItem().Line;

        line.ShouldNotContain("\n");

        using var document = JsonDocument.Parse(line);

        document.RootElement.GetProperty("type").GetString().ShouldBe("run");
        document.RootElement.GetProperty("runId").GetString()
            .ShouldBe(RunResultFixture.FixedRunId.ToString());
    }

    [Fact]
    public async Task WriteExternalAsync_WritesTheMeasurementAsOneLine()
    {
        await Writer().WriteExternalAsync(
            Workspace,
            new HistorySettings(".preflight/history", HistoryMode.Shared),
            new ExternalMeasurement("build", RunResultFixture.FixedStart, TimeSpan.FromMinutes(38), 0, "msbuild"),
            TestContext.Current.CancellationToken);

        var line = _store.Appended.ShouldHaveSingleItem().Line;

        using var document = JsonDocument.Parse(line);

        document.RootElement.GetProperty("type").GetString().ShouldBe("external");
        document.RootElement.GetProperty("label").GetString().ShouldBe("build");
    }

    private NdjsonHistoryWriter Writer() => new(_store, Machine, _clock);
}
