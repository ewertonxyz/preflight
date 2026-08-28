namespace Preflight.Core.Tests.History;

using Preflight.Abstractions.Model;
using Preflight.Core.History;

/// <summary>
/// Reading the NDJSON back, including the lines that cannot be read.
/// </summary>
public sealed class NdjsonHistoryReaderTests
{
    private const string Directory = "/w/.preflight/history";

    private const string RunLine =
        """{"type":"run","runId":"a","startedAt":"2026-08-26T14:30:00+00:00","durationMs":1840,""" +
        """ "stage":"BuildReadiness","verdict":"Blocked","partial":false,"failOnWarning":false,""" +
        """ "noSkip":false,"executedCount":1,"executions":[{"ruleId":"core.build.configuration",""" +
        """ "status":"Failed","durationMs":600}]}""";

    private const string ExternalLine =
        """{"type":"external","label":"build","startedAt":"2026-08-26T13:00:00+00:00",""" +
        """ "durationMs":2280000,"exitCode":0,"command":"msbuild Game.sln"}""";

    private readonly InMemoryHistoryFiles _files = new();

    /// <summary>
    /// A directory that is not there yields nothing rather than throwing.
    /// </summary>
    /// <remarks>
    /// The natural implementation throws <c>DirectoryNotFoundException</c>, which
    /// would land on exit 3 — and the exit-code contract makes an empty history a valid
    /// answer, not an internal error. It is the ordinary state of a workspace
    /// nobody has validated yet.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_WhenTheDirectoryIsAbsent_YieldsNothing()
    {
        _files.DirectoryIsThere = false;

        (await ReadAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadAsync_ForARunAndAMeasurement_ParsesBoth()
    {
        _files.Add("2026-08.WKS-1234.ndjson", RunLine + "\n" + ExternalLine + "\n");

        var entries = await ReadAsync();

        var run = entries[0].ShouldBeOfType<HistoryEntry.Parsed>().Value.ShouldBeOfType<HistoryEvent.Run>();

        run.Duration.ShouldBe(TimeSpan.FromMilliseconds(1840));
        run.Stage.ShouldBe(ValidationStage.BuildReadiness);
        run.Verdict.ShouldBe(RunVerdict.Blocked);
        run.ExecutedCount.ShouldBe(1);
        run.Executions.ShouldHaveSingleItem().ShouldBe(
            new HistoryExecution("core.build.configuration", RuleStatus.Failed, TimeSpan.FromMilliseconds(600)));

        var external = entries[1].ShouldBeOfType<HistoryEntry.Parsed>().Value
            .ShouldBeOfType<HistoryEvent.External>();

        external.Label.ShouldBe("build");
        external.ExitCode.ShouldBe(0);
        external.Duration.ShouldBe(TimeSpan.FromMilliseconds(2280000));
    }

    /// <summary>
    /// The blank line at the end of every append-only file is not damage.
    /// </summary>
    /// <remarks>
    /// Every real history file ends in a terminator, so the last read of every
    /// real history file is an empty line. Counting it would report corruption
    /// in every file nothing had corrupted — which is worse than useless,
    /// because the report prints that count.
    /// </remarks>
    [Theory]
    [InlineData("\n")]
    [InlineData("\n\n")]
    [InlineData("\n   \n")]
    public async Task ReadAsync_ForAFileEndingInATerminator_DoesNotYieldABlankRecord(string tail)
    {
        _files.Add("2026-08.WKS-1234.ndjson", RunLine + tail);

        (await ReadAsync()).ShouldHaveSingleItem().ShouldBeOfType<HistoryEntry.Parsed>();
    }

    /// <remarks>
    /// The interleaved line of the history format's third row. It is skipped and
    /// reported, never swallowed: publishing percentiles over an
    /// unknown fraction of the sample the thing this refuses to do.
    /// </remarks>
    [Theory]
    [InlineData("""{"type":"run","startedAt":"2026""")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"runId":"a"}""")]
    [InlineData("""{"type":"run","durationMs":1}""")]
    [InlineData("""{"type":"run","startedAt":"not-a-date","durationMs":1,"stage":"Workspace","verdict":"Passed"}""")]
    [InlineData("""{"type":"run","startedAt":"2026-08-26T14:30:00+00:00","stage":"Workspace","verdict":"Passed"}""")]
    [InlineData("""{"type":"run","startedAt":"2026-08-26T14:30:00+00:00","durationMs":"soon","stage":"Workspace","verdict":"Passed"}""")]
    [InlineData("""{"type":"run","startedAt":"2026-08-26T14:30:00+00:00","durationMs":1,"verdict":"Passed"}""")]
    [InlineData("""{"type":"run","startedAt":"2026-08-26T14:30:00+00:00","durationMs":1,"stage":"Nowhere","verdict":"Passed"}""")]
    [InlineData("""{"type":"run","startedAt":"2026-08-26T14:30:00+00:00","durationMs":1,"stage":"Workspace"}""")]
    [InlineData("""{"type":"run","startedAt":"2026-08-26T14:30:00+00:00","durationMs":1,"stage":"Workspace","verdict":"Sideways"}""")]
    [InlineData("""{"type":"external","startedAt":"2026-08-26T14:30:00+00:00","durationMs":1,"exitCode":0}""")]
    [InlineData("""{"type":"external","startedAt":"2026-08-26T14:30:00+00:00","durationMs":1,"label":"build"}""")]

    // A well-formed line whose values are the wrong shape. Each of these is a
    // separate way a foreign or damaged writer produces JSON that parses and
    // does not mean anything: a verdict that is a number, a duration that is
    // not a whole millisecond, and an enum value outside the declared set.
    [InlineData("""{"type":"run","startedAt":"2026-08-26T14:30:00+00:00","durationMs":1,"stage":"Workspace","verdict":123}""")]
    [InlineData("""{"type":"run","startedAt":"2026-08-26T14:30:00+00:00","durationMs":1.5,"stage":"Workspace","verdict":"Passed"}""")]
    [InlineData("""{"type":"run","startedAt":"2026-08-26T14:30:00+00:00","durationMs":1,"stage":"Workspace","verdict":"99"}""")]
    public async Task ReadAsync_ForALineItCannotUnderstand_ReportsItAsUnreadable(string line)
    {
        _files.Add("2026-08.WKS-1234.ndjson", line + "\n");

        var unreadable = (await ReadAsync()).ShouldHaveSingleItem().ShouldBeOfType<HistoryEntry.Unreadable>();

        unreadable.File.ShouldBe("2026-08.WKS-1234.ndjson");
        unreadable.Line.ShouldBe(1);
    }

    /// <remarks>
    /// A run whose execution list is malformed is unreadable as a whole. Half of
    /// it would contribute a duration to the percentiles and a wrong count to
    /// "slowest rules" in the same record.
    /// </remarks>
    [Theory]
    [InlineData("""[{"status":"Passed","durationMs":1}]""")]
    [InlineData("""[{"ruleId":"a","durationMs":1}]""")]
    [InlineData("""[{"ruleId":"a","status":"Sideways","durationMs":1}]""")]
    [InlineData("""[{"ruleId":"a","status":"Passed"}]""")]
    [InlineData("""["core.build.configuration"]""")]
    public async Task ReadAsync_ForARunWithAMalformedExecution_ReportsTheWholeLineAsUnreadable(string executions)
    {
        _files.Add("2026-08.WKS-1234.ndjson",
            """{"type":"run","startedAt":"2026-08-26T14:30:00+00:00","durationMs":1,"stage":"Workspace",""" +
            """ "verdict":"Passed","executions":""" + executions + "}\n");

        (await ReadAsync()).ShouldHaveSingleItem().ShouldBeOfType<HistoryEntry.Unreadable>();
    }

    /// <remarks>
    /// A record replaced by the 64 KB summary of the history format still carries the
    /// three fields per execution this reads, and the flags it omits read as
    /// false rather than as damage.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_ForARecordWithoutTheOptionalFlags_ReadsThemAsFalse()
    {
        _files.Add("2026-08.WKS-1234.ndjson",
            """{"type":"run","startedAt":"2026-08-26T14:30:00+00:00","durationMs":1,""" +
            """ "stage":"Workspace","verdict":"Passed"}""" + "\n");

        var run = (await ReadAsync()).ShouldHaveSingleItem()
            .ShouldBeOfType<HistoryEntry.Parsed>().Value.ShouldBeOfType<HistoryEvent.Run>();

        run.Partial.ShouldBeFalse();
        run.FailOnWarning.ShouldBeFalse();
        run.NoSkip.ShouldBeFalse();
        run.ExecutedCount.ShouldBe(0);
        run.Executions.ShouldBeEmpty();
    }

    /// <summary>
    /// A recorded execution says whether it was a run or a lookup.
    /// </summary>
    /// <remarks>
    /// The report builds "slowest rules" out of these durations, and a cache
    /// hit contributes nought seconds. Without the flag the ranking collapses
    /// towards whichever rule caches best — and the measurement named as
    /// the trigger for building the cache would be destroyed by the cache.
    /// </remarks>
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task ReadAsync_ForAnExecution_CarriesTheFromCacheFlag(string raw, bool expected)
    {
        _files.Add("2026-08.WKS-1234.ndjson",
            """{"type":"run","startedAt":"2026-08-26T14:30:00+00:00","durationMs":1,"stage":"Workspace",""" +
            """ "verdict":"Passed","executions":[{"ruleId":"core.a.alpha","status":"Passed",""" +
            """ "durationMs":0,"fromCache":""" + raw + "}]}\n");

        var run = (await ReadAsync()).ShouldHaveSingleItem()
            .ShouldBeOfType<HistoryEntry.Parsed>().Value.ShouldBeOfType<HistoryEvent.Run>();

        run.Executions.ShouldHaveSingleItem().FromCache.ShouldBe(expected);
    }

    /// <remarks>
    /// An older record has no such field, and reads as a real run — which it
    /// was, because the cache did not exist when it was written.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_ForARecordWrittenBeforeTheCacheExisted_ReadsAsNotCached()
    {
        _files.Add("2026-08.WKS-1234.ndjson",
            """{"type":"run","startedAt":"2026-08-26T14:30:00+00:00","durationMs":1,"stage":"Workspace",""" +
            """ "verdict":"Passed","executions":[{"ruleId":"core.a.alpha","status":"Passed",""" +
            """ "durationMs":600}]}""" + "\n");

        var run = (await ReadAsync()).ShouldHaveSingleItem()
            .ShouldBeOfType<HistoryEntry.Parsed>().Value.ShouldBeOfType<HistoryEvent.Run>();

        run.Executions.ShouldHaveSingleItem().FromCache.ShouldBeFalse();
    }

    /// <remarks>
    /// Kept apart from an unreadable line because it means something different:
    /// not damage, but a writer this version has not met. It is what lets a
    /// later phase add an event type without invalidating the history on disk.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_ForAnEventTypeItDoesNotKnow_IgnoresItByName()
    {
        // Deliberately a type nothing will ever write. It used to be "cache",
        // which stopped being a good specimen the day the incremental cache
        // existed: a test whose subject is "an event type this version does not
        // know" must not name a subsystem that might one day write one.
        _files.Add("2026-08.WKS-1234.ndjson", """{"type":"telepathy","hits":3}""" + "\n");

        (await ReadAsync()).ShouldHaveSingleItem()
            .ShouldBeOfType<HistoryEntry.Ignored>().Type.ShouldBe("telepathy");
    }

    /// <summary>
    /// Files are read in ordinal order of their path, whatever order the file
    /// system offered them in.
    /// </summary>
    /// <remarks>
    /// <c>Directory.EnumerateFiles</c> promises nothing about order, and a
    /// report whose contents depend on it is not the diffable output the determinism guarantee
    /// asks for.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_AcrossFilesOfferedOutOfOrder_ReadsThemOrdinally()
    {
        _files.Add("2026-09.WKS-1234.ndjson", Line("2026-09-01T00:00:00+00:00"));
        _files.Add("2026-07.BUILD-07.ndjson", Line("2026-07-01T00:00:00+00:00"));
        _files.Add("2026-08.WKS-1234.ndjson", Line("2026-08-01T00:00:00+00:00"));

        var months = (await ReadAsync())
            .Select(entry => ((HistoryEvent.Run)((HistoryEntry.Parsed)entry).Value).StartedAt.Month);

        months.ShouldBe([7, 8, 9]);
    }

    private static string Line(string startedAt) =>
        $$"""{"type":"run","startedAt":"{{startedAt}}","durationMs":1,"stage":"Workspace","verdict":"Passed"}""" +
        "\n";

    private async Task<IReadOnlyList<HistoryEntry>> ReadAsync()
    {
        var entries = new List<HistoryEntry>();

        await foreach (var entry in new NdjsonHistoryReader(_files)
                           .ReadAsync(Directory, TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        return entries;
    }
}
