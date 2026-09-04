namespace Preflight.Cli.Tests.Commands;

using System.Text;
using System.Text.Json;
using Preflight.Cli.Commands;
using Preflight.Cli.Model;
using Preflight.Core;
using Preflight.Core.Execution;
using Preflight.Core.History;
using Preflight.TestSupport;

/// <summary>
/// <c>preflight measure</c>, the transparent wrapper.
/// </summary>
/// <remarks>
/// Every test here is about the wrapper changing nothing: not the exit code,
/// not the bytes, not the arguments. A measurement that alters what it measures
/// is useless, and the ways to alter it are all small.
/// </remarks>
public sealed class MeasureCommandTests : IDisposable
{
    private readonly DirectoryInfo _workspace = Directory.CreateTempSubdirectory("preflight-measure-");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly MemoryStream _rawOutput = new();
    private readonly MemoryStream _rawError = new();

    private readonly FixedTimeProvider _clock =
        new(new DateTimeOffset(2026, 8, 26, 14, 30, 0, TimeSpan.Zero));

    public void Dispose()
    {
        _workspace.Delete(recursive: true);
        _output.Dispose();
        _error.Dispose();
        _rawOutput.Dispose();
        _rawError.Dispose();
    }

    /// <summary>
    /// Whatever the child returned is what preflight returns.
    /// </summary>
    /// <remarks>
    /// The 2 in this table is the row that matters. The exit-code contract gives preflight
    /// its own 2 for a refused invocation, and <c>measure</c> has to be able to
    /// return a child's 2 without it meaning that.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(42)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(255)]
    public void Measure_ForEachChildExitCode_ReturnsItVerbatim(int childExitCode)
    {
        var launcher = new RecordingLauncher { ExitCode = childExitCode };

        Invoke(launcher, "measure", "--label", "build", "--", "msbuild").ShouldBe(childExitCode);

        _error.ToString().ShouldBeEmpty();
    }

    /// <remarks>
    /// The bytes are asserted rather than the text. A wrapper that decoded and
    /// re-encoded would pass a string comparison on an ASCII fixture and corrupt
    /// the first build log that was not.
    /// </remarks>
    [Fact]
    public void Measure_ForAChildWritingToBothStreams_PropagatesTheBytesUnchanged()
    {
        var outputBytes = Encoding.UTF8.GetBytes("Build succeeded. caf\u00e9\n");
        var errorBytes = Encoding.UTF8.GetBytes("warning MSB0001: n\u00e3o\n");

        var launcher = new RecordingLauncher
        {
            StandardOutput = outputBytes,
            StandardError = errorBytes,
        };

        Invoke(launcher, "measure", "--label", "build", "--", "msbuild").ShouldBe(0);

        _rawOutput.ToArray().ShouldBe(outputBytes);
        _rawError.ToArray().ShouldBe(errorBytes);
    }

    /// <remarks>
    /// Exit 2 and the child never started. Both refusals happen before the
    /// launch, which is what keeps 2 and 127 distinct.
    /// </remarks>
    [Theory]
    [InlineData("measure|--|msbuild")]
    [InlineData("measure|--label|build")]
    [InlineData("measure|--label|build|--")]
    [InlineData("measure|--label||--|msbuild")]
    public void Measure_WithARefusedInvocation_IsTwoAndNeverStartsTheChild(string arguments)
    {
        var launcher = new RecordingLauncher();

        // Pipes rather than spaces, because one of the rows is an empty --label
        // and a space-split cannot express one.
        Invoke(launcher, arguments.Split('|')).ShouldBe(2);

        launcher.Started.ShouldBeFalse();
    }

    /// <summary>
    /// A child that cannot be started is 127, not 2.
    /// </summary>
    /// <remarks>
    /// <c>ProcessLaunchException</c> is a <c>ConfigurationLoadException</c>
    /// and would otherwise reach the CLI's own catch and become 2 — which would
    /// make a typo in the measured binary indistinguishable from a typo in
    /// preflight's own flags.
    /// </remarks>
    [Fact]
    public void Measure_WithAChildThatCannotBeStarted_IsOneTwentySeven()
    {
        var launcher = new RecordingLauncher
        {
            Failure = new ProcessLaunchException("'msbuild' could not be started: not found"),
        };

        Invoke(launcher, "measure", "--label", "build", "--", "msbuild")
            .ShouldBe(ExitCode.ChildNotStarted);

        _error.ToString().ShouldContain("msbuild");
    }

    /// <remarks>
    /// The arguments after <c>--</c> belong to the child, including the ones
    /// that happen to spell preflight's own flags. Consuming them would change
    /// the command being measured, which is the one thing this command must not
    /// do.
    /// </remarks>
    [Fact]
    public void Measure_WithChildArgumentsThatSpellPreflightFlags_PassesThemThrough()
    {
        var launcher = new RecordingLauncher();

        Invoke(launcher, "measure", "--label", "build", "--", "git", "--no-local", "--set", "x=y")
            .ShouldBe(0);

        var request = launcher.Request.ShouldNotBeNull();

        request.FileName.ShouldBe("git");
        request.Arguments.ShouldBe(["--no-local", "--set", "x=y"]);
    }

    /// <remarks>
    /// The duration comes from the injected clock, so the recorded number is the
    /// one the test chose rather than how long the test host took.
    /// </remarks>
    [Fact]
    public void Measure_RecordsTheMeasurementInTheHistory()
    {
        var launcher = new RecordingLauncher { ExitCode = 0 };

        Invoke(launcher, "measure", "--label", "build", "--", "msbuild", "Game.sln").ShouldBe(0);

        using var document = JsonDocument.Parse(HistoryLines().ShouldHaveSingleItem());

        var root = document.RootElement;

        root.GetProperty("type").GetString().ShouldBe("external");
        root.GetProperty("label").GetString().ShouldBe("build");
        root.GetProperty("exitCode").GetInt32().ShouldBe(0);
        root.GetProperty("command").GetString().ShouldBe("msbuild Game.sln");
        root.GetProperty("startedAt").GetDateTimeOffset().ShouldBe(_clock.GetUtcNow());
    }

    /// <remarks>
    /// The history format: instrumentation is subordinate to the function it
    /// instruments. A wrapper that turned a child's 7 into a 3 because a
    /// partition filled up would be worse than one that recorded nothing.
    /// </remarks>
    [Fact]
    public void Measure_WhenTheHistoryCannotBeWritten_StillReturnsTheChildsExitCode()
    {
        var launcher = new RecordingLauncher { ExitCode = 7 };

        Invoke(launcher, new FailingHistoryStore(new IOException("disk full")), "measure", "--label", "build", "--", "msbuild")
            .ShouldBe(7);

        _error.ToString().ShouldContain("disk full");
    }

    /// <summary>
    /// A policy the command cannot read is exit 2, before the child starts.
    /// </summary>
    /// <remarks>
    /// One validation regime, not two. The alternative — resolving
    /// only the root keys for this command — would mean the CLI accepted broken
    /// configuration for some commands and not others.
    /// </remarks>
    [Fact]
    public void Measure_OverAnInvalidPolicy_IsTwoAndNeverStartsTheChild()
    {
        File.WriteAllText(
            Path.Combine(_workspace.FullName, "preflight.base.json"),
            """{ "schemaVersion": 1, "historyMode": "nonsense" }""");

        var launcher = new RecordingLauncher();

        Invoke(launcher, "measure", "--label", "build", "--", "msbuild").ShouldBe(2);

        launcher.Started.ShouldBeFalse();
    }

    private IReadOnlyList<string> HistoryLines() =>
    [
        .. Directory
            .EnumerateFiles(Path.Combine(_workspace.FullName, ".preflight", "history"), "*.ndjson")
            .Order(StringComparer.Ordinal)
            .SelectMany(File.ReadAllLines),
    ];

    private int Invoke(RecordingLauncher launcher, params string[] args) =>
        Invoke(launcher, null, args);

    private int Invoke(RecordingLauncher launcher, IHistoryStore? history, params string[] args) =>
        PreflightCommandLine.Execute(
            args,
            _output,
            _error,
            parse => CommandDispatcher.Run(parse, CommandEnvironments.For(
                _workspace,
                _output,
                _error,
                _clock,
                children: launcher,
                history: history,
                rawOutput: _rawOutput,
                rawError: _rawError)));
}
