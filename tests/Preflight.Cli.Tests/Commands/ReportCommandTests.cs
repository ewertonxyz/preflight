namespace Preflight.Cli.Tests.Commands;

using Preflight.TestSupport;

/// <summary>
/// <c>preflight report</c> end to end, over a history on real disk.
/// </summary>
/// <remarks>
/// The renderer has golden files and the aggregation has its own tests. What
/// neither can show is whether the command reads the history the policy points
/// at, in the window it was given.
/// </remarks>
public sealed class ReportCommandTests : IDisposable
{
    private readonly DirectoryInfo _workspace = Directory.CreateTempSubdirectory("preflight-report-");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    private readonly FixedTimeProvider _clock =
        new(new DateTimeOffset(2026, 8, 26, 14, 30, 0, TimeSpan.Zero));

    public void Dispose()
    {
        _workspace.Delete(recursive: true);
        _output.Dispose();
        _error.Dispose();
    }

    /// <remarks>
    /// The exit-code contract: an empty history is a valid answer, not an error. This is
    /// also the first-run case, where the directory does not exist at all.
    /// </remarks>
    [Fact]
    public void Report_OverAWorkspaceWithNoHistory_IsZeroAndSaysSo()
    {
        Invoke("report", "--since", "30d").ShouldBe(0);

        _output.ToString().ShouldContain("Nothing recorded in this window");
    }

    [Fact]
    public void Report_ReadsTheRecordedRuns()
    {
        WriteHistory(
            Run("2026-08-26T13:00:00+00:00", "Passed"),
            Run("2026-08-26T12:00:00+00:00", "Blocked"));

        Invoke("report", "--since", "30d").ShouldBe(0);

        var text = _output.ToString();

        text.ShouldContain("Preflight history");
        text.ShouldContain("Runs");
        text.ShouldContain("Blocking verdicts at BuildReadiness");
    }

    /// <remarks>
    /// The window is honoured, and the clock it is measured back from is the
    /// injected one — otherwise this test would start failing on its own the day
    /// after it was written.
    /// </remarks>
    [Fact]
    public void Report_ForARunOutsideTheWindow_LeavesItOut()
    {
        WriteHistory(Run("2026-07-01T00:00:00+00:00", "Blocked"));

        Invoke("report", "--since", "1d").ShouldBe(0);

        _output.ToString().ShouldContain("Nothing recorded in this window");
    }

    /// <remarks>
    /// Publishing percentiles over an unknown fraction of the sample is the
    /// tool's own reporting claiming more than it measured, so the count of
    /// skipped lines is printed beside the <c>n</c>.
    /// </remarks>
    [Fact]
    public void Report_OverAHistoryWithADamagedLine_SaysHowManyItSkipped()
    {
        WriteHistory(Run("2026-08-26T13:00:00+00:00", "Passed"), "{\"type\":\"run\",\"start");

        Invoke("report", "--since", "30d").ShouldBe(0);

        _output.ToString().ShouldContain("1 history line could not be read and was skipped");
    }

    /// <remarks>
    /// The window is mandatory, and a refusal is exit 2.
    /// A report over a window nobody chose is a number nobody asked for.
    /// </remarks>
    [Theory]
    [InlineData("report")]
    [InlineData("report|--since|30x")]
    [InlineData("report|--since|0d")]
    public void Report_WithoutAWindowItAccepts_IsTwo(string arguments)
    {
        Invoke(arguments.Split('|')).ShouldBe(2);

        _error.ToString().ShouldContain("--since");
    }

    /// <remarks>
    /// The history lives where policy says it lives, which is what makes the
    /// network-share escape of the history format usable at all.
    /// </remarks>
    [Fact]
    public void Report_ReadsTheHistoryPathThePolicyNames()
    {
        File.WriteAllText(
            Path.Combine(_workspace.FullName, "preflight.base.json"),
            """{ "schemaVersion": 1, "historyPath": "build/history" }""");

        var directory = Path.Combine(_workspace.FullName, "build", "history");

        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "2026-08.WKS-1234.ndjson"),
            Run("2026-08-26T13:00:00+00:00", "Blocked") + "\n");

        Invoke("report", "--since", "30d").ShouldBe(0);

        _output.ToString().ShouldContain("Blocking verdicts at BuildReadiness");
    }

    /// <summary>
    /// A <c>--format</c> this command does not implement is exit 2.
    /// </summary>
    /// <remarks>
    /// A sibling of <see cref="Report_WithoutAWindowItAccepts_IsTwo"/> rather
    /// than another row inside it: folding the two together would file the
    /// refusal of <c>--format</c> under a name that only mentions
    /// <c>--since</c>. <c>Json</c> with a capital letter is here because the
    /// comparison is ordinal, and a nearly-right flag is a wrong flag.
    /// </remarks>
    [Theory]
    [InlineData("report|--since|30d|--format|bogus")]
    [InlineData("report|--since|30d|--format|Json")]
    [InlineData("report|--since|30d|--format|sarif")]
    public void Report_WithAFormatItDoesNotAccept_IsTwo(string arguments) =>
        Invoke(arguments.Split('|')).ShouldBe(2);

    /// <summary>
    /// The JSON form is a document, not the screen with JSON at the end of it.
    /// </summary>
    /// <remarks>
    /// The renderer has golden files. What only this can show is that the
    /// command chose the renderer at all, and that nothing else reached standard
    /// output alongside it — which is what makes the output parseable rather
    /// than merely present.
    /// </remarks>
    [Fact]
    public void Report_WithFormatJson_WritesParseableJsonOverTheRecordedRuns()
    {
        WriteHistory(
            Run("2026-08-26T13:00:00+00:00", "Passed"),
            Run("2026-08-26T12:00:00+00:00", "Blocked"));

        Invoke("report", "--since", "30d", "--format", "json").ShouldBe(0);

        var rendered = _output.ToString();

        Should.NotThrow(() => System.Text.Json.JsonDocument.Parse(rendered));

        System.Text.Json.JsonDocument.Parse(rendered)
            .RootElement
            .GetProperty("runCount")
            .GetInt32()
            .ShouldBe(2);

        rendered.ShouldNotContain("Preflight history");
    }

    /// <summary>
    /// <c>console</c> is the default and prints exactly what it printed before
    /// the flag existed.
    /// </summary>
    /// <remarks>
    /// The guard on the conversion of <c>ReportOptions</c> from a positional
    /// record to named required properties. A call site that lost a value in
    /// that conversion would change this screen, and nothing else in the suite
    /// compares the two invocations byte for byte.
    /// </remarks>
    [Fact]
    public void Report_WithFormatConsole_IsByteIdenticalToTheCommandWithoutAFormat()
    {
        WriteHistory(
            Run("2026-08-26T13:00:00+00:00", "Passed"),
            Run("2026-08-26T12:00:00+00:00", "Blocked"));

        Invoke("report", "--since", "30d").ShouldBe(0);
        var first = _output.ToString();

        _output.GetStringBuilder().Clear();

        Invoke("report", "--since", "30d", "--format", "console").ShouldBe(0);

        _output.ToString().ShouldBe(first);
    }

    private static string Run(string startedAt, string verdict) =>
        $$"""{"type":"run","startedAt":"{{startedAt}}","durationMs":1840,"stage":"BuildReadiness",""" +
        $$""" "verdict":"{{verdict}}","executions":[]}""";

    private void WriteHistory(params string[] lines)
    {
        var directory = Path.Combine(_workspace.FullName, ".preflight", "history");

        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "2026-08.WKS-1234.ndjson"),
            string.Join("\n", lines) + "\n");
    }

    private int Invoke(params string[] args) => PreflightCommandLine.Execute(
        args,
        _output,
        _error,
        parse => CommandDispatcher.Run(
            parse,
            CommandEnvironments.For(_workspace, _output, _error, _clock)));
}
