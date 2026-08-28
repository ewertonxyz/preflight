namespace Preflight.Core.Tests.History;

using System.Text;
using System.Text.Json;
using Preflight.Abstractions;
using Preflight.Core.History;
using Preflight.TestSupport;

/// <summary>
/// The 64 KB cap of the history format, and the two ways an
/// append-only format loses data.
/// </summary>
public sealed class HistoryLineTests
{
    [Fact]
    public void MaxBytes_IsTheLimitSection101States() => HistoryLine.MaxBytes.ShouldBe(65536);

    /// <summary>
    /// The boundary is exact, and only what is over it is replaced.
    /// </summary>
    /// <remarks>
    /// An off-by-one in a cap does not throw. It either replaces a record that
    /// fitted, losing detail nothing asked to lose, or lets one through that did
    /// not — and the second is the interleaved line the cap exists to prevent.
    /// </remarks>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void ForRun_AtTheByteBoundary_ReplacesOnlyWhatIsOverIt(int offset, bool truncated)
    {
        var line = HistoryLine.ForRun(RunOfSize(HistoryLine.MaxBytes + offset));

        line.Contains("\"truncated\":true", StringComparison.Ordinal).ShouldBe(truncated);
        Encoding.UTF8.GetByteCount(line).ShouldBeLessThanOrEqualTo(HistoryLine.MaxBytes);
    }

    /// <summary>
    /// The cap is measured on the record as it will be written.
    /// </summary>
    /// <remarks>
    /// An accented message costs six bytes a character once the serialiser has
    /// escaped it, not one — so a rule flooding findings in any language other
    /// than English reaches the limit six times sooner than its character count
    /// suggests. Measuring the serialised form rather than the model is what
    /// makes that automatic instead of a special case.
    /// </remarks>
    [Fact]
    public void ForRun_ForANonAsciiMessage_MeasuresItsEscapedForm()
    {
        var accented = new string('\u00e9', HistoryLine.MaxBytes / 4);

        // A quarter of the limit in characters, over it once escaped.
        accented.Length.ShouldBeLessThan(HistoryLine.MaxBytes);

        HistoryLine.ForRun(RunWithMessage(accented)).ShouldContain("\"truncated\":true");
    }

    /// <summary>
    /// A replaced record is still valid JSON, still carries the duration, and
    /// still says which rule produced the flood.
    /// </summary>
    /// <remarks>
    /// Cutting bytes off the end would split a code point and take the
    /// heaviest run — the one the report most wants in its percentiles — out
    /// of the history altogether.
    /// </remarks>
    [Fact]
    public void ForRun_ForATruncatedRecord_IsStillValidJsonCarryingTheDurationAndFindingCounts()
    {
        using var document = JsonDocument.Parse(HistoryLine.ForRun(RunOfSize(HistoryLine.MaxBytes * 2)));

        var root = document.RootElement;

        root.GetProperty("type").GetString().ShouldBe("run");
        root.GetProperty("durationMs").GetInt64().ShouldBe(1000);
        root.GetProperty("stage").GetString().ShouldBe("BuildReadiness");
        root.GetProperty("verdict").GetString().ShouldBe("Blocked");
        root.GetProperty("executedCount").GetInt32().ShouldBe(1);
        root.GetProperty("truncated").GetBoolean().ShouldBeTrue();
        root.GetProperty("findingCounts").GetProperty("core.presubmit.large-file").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// A finding containing a line break still produces exactly one line.
    /// </summary>
    /// <remarks>
    /// This is the second way an append-only format loses data, and unlike the
    /// interleaved write of the history format it happens on a local disk with a
    /// single process. U+2028 is included because it is a line terminator to a
    /// JavaScript reader and not to a C# one.
    /// </remarks>
    [Fact]
    public void ForRun_ForAFindingContainingLineBreaks_ProducesExactlyOneLine()
    {
        var message = "first\nsecond\r\nthird\u2028fourth";
        var line = HistoryLine.ForRun(RunWithMessage(message));

        line.ShouldNotContain("\n");
        line.ShouldNotContain("\r");

        using var document = JsonDocument.Parse(line);

        document.RootElement
            .GetProperty("executions")[0]
            .GetProperty("findings")[0]
            .GetProperty("message")
            .GetString()
            .ShouldBe(message);
    }

    [Fact]
    public void ForExternal_IsTheDocumentedShape()
    {
        using var document = JsonDocument.Parse(HistoryLine.ForExternal(new ExternalMeasurement(
            "build",
            RunResultFixture.FixedStart,
            TimeSpan.FromMinutes(38),
            0,
            "msbuild Game.sln")));

        var root = document.RootElement;

        root.GetProperty("type").GetString().ShouldBe("external");
        root.GetProperty("label").GetString().ShouldBe("build");
        root.GetProperty("durationMs").GetInt64().ShouldBe(2280000);
        root.GetProperty("exitCode").GetInt32().ShouldBe(0);
        root.GetProperty("command").GetString().ShouldBe("msbuild Game.sln");
    }

    /// <summary>
    /// A run whose serialised record is exactly <paramref name="bytes"/> long.
    /// </summary>
    /// <remarks>
    /// The padding is ASCII, so one character is one byte of JSON and the size
    /// is arithmetic rather than a search. A boundary test that had to bisect
    /// its way to the limit would be asserting the search as much as the cap.
    /// </remarks>
    private static RunResult RunOfSize(int bytes) =>
        RunWithMessage(new string('x', bytes - Serialised(RunWithMessage(string.Empty)).Length));

    private static string Serialised(RunResult run) =>
        JsonSerializer.Serialize(RunEventDocument.For(run), RunEventDocument.SingleLine);

    private static RunResult RunWithMessage(string message) => RunResultFixture.With(
        RunResultFixture.Execution("core.presubmit.large-file", RuleStatus.Failed, 0.4) with
        {
            Findings = [new Finding { Message = message }],
        });
}
