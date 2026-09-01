namespace Preflight.Cli.Tests.Commands;

using System.Text.Json;
using Preflight.TestSupport;

/// <summary>
/// What <c>preflight run</c> leaves behind, and what it refuses to let that
/// cost.
/// </summary>
/// <remarks>
/// The history format spends a paragraph on the second half: a failure to write the
/// history does <b>not</b> alter the verdict or the exit code, because
/// instrumentation is subordinate to the function it instruments and a full
/// partition must not turn a <c>Passed</c> into an error.
/// </remarks>
public sealed class RunHistoryTests : IDisposable
{
    private readonly DirectoryInfo _workspace = Directory.CreateTempSubdirectory("preflight-run-history-");
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

    [Fact]
    public void Run_ForARunThatReachedAVerdict_AppendsExactlyOneRunEvent()
    {
        GoodWorkspace();

        Invoke("run", "--stage", "workspace").ShouldBe(0);

        var line = HistoryLines().ShouldHaveSingleItem();

        using var document = JsonDocument.Parse(line);

        document.RootElement.GetProperty("type").GetString().ShouldBe("run");
        document.RootElement.GetProperty("stage").GetString().ShouldBe("Workspace");
        document.RootElement.GetProperty("executedCount").GetInt32().ShouldBeGreaterThan(0);
    }

    /// <remarks>
    /// The file name is the one the history format documents, built from the injected
    /// clock and machine rather than from whichever month the test ran in.
    /// </remarks>
    [Fact]
    public void Run_WritesToTheFileSection101Names()
    {
        GoodWorkspace();

        Invoke("run", "--stage", "workspace").ShouldBe(0);

        Directory
            .EnumerateFiles(HistoryDirectory(), "*.ndjson")
            .Select(Path.GetFileName)
            .ShouldBe(["2026-08.WKS-1234.ndjson"]);
    }

    /// <summary>
    /// A run twice over appends rather than replaces.
    /// </summary>
    [Fact]
    public void Run_Twice_AppendsBothRuns()
    {
        GoodWorkspace();

        Invoke("run", "--stage", "workspace").ShouldBe(0);
        Invoke("run", "--stage", "workspace").ShouldBe(0);

        HistoryLines().Count.ShouldBe(2);
    }

    /// <summary>
    /// The commands that execute nothing leave no trace.
    /// </summary>
    /// <remarks>
    /// an event for a command that never ran a rule would pollute the
    /// duration sample with invocations that validated nothing, and the history format
    /// creates the directory on the first write — so a <c>preflight
    /// rules</c> in a clean workspace has to leave it clean.
    /// </remarks>
    [Theory]
    [InlineData("rules")]
    [InlineData("graph")]
    public void InspectionCommands_WriteNothingAndCreateNoDirectory(string command)
    {
        GoodWorkspace();

        Invoke(command).ShouldBe(0);

        Directory.Exists(HistoryDirectory()).ShouldBeFalse();
    }

    /// <summary>
    /// A history that cannot be written changes nothing but standard error.
    /// </summary>
    /// <remarks>
    /// The exit code and standard output are compared against the same run with
    /// a working history, byte for byte. Asserting only the exit code would pass
    /// against an implementation that swallowed the failure and also swallowed
    /// half the report.
    /// </remarks>
    [Theory]
    [InlineData("workspace")]
    [InlineData("build-readiness")]
    public void Run_WhenTheHistoryCannotBeWritten_KeepsTheExitCodeAndTheReportBytes(string stage)
    {
        GoodWorkspace();

        var expectedCode = Invoke("run", "--stage", stage);
        var expectedReport = _output.ToString();

        _output.GetStringBuilder().Clear();
        _error.GetStringBuilder().Clear();

        var actualCode = Invoke(
            new FailingHistoryStore(new IOException("There is not enough space on the disk.")),
            "run",
            "--stage",
            stage);

        actualCode.ShouldBe(expectedCode);
        _output.ToString().ShouldBe(expectedReport);
        _error.ToString().ShouldContain("There is not enough space on the disk.");
    }

    /// <remarks>
    /// The second of the two ways a disk says no, named separately because a
    /// general catch would also hide a defect in the serialiser — which is not a
    /// disk problem and must not be reported as one.
    /// </remarks>
    [Fact]
    public void Run_WhenTheHistoryDirectoryIsNotWritable_StillSucceeds()
    {
        GoodWorkspace();

        Invoke(new FailingHistoryStore(new UnauthorizedAccessException("Access to the path is denied.")),
            "run", "--stage", "workspace")
            .ShouldBe(0);

        _error.ToString().ShouldContain("Access to the path is denied.");
    }

    /// <remarks>
    /// The history goes where policy says. The history format's escape for a network
    /// share is a policy key, and a run that ignored it would write to the
    /// workspace anyway.
    /// </remarks>
    [Fact]
    public void Run_WritesToTheHistoryPathThePolicyNames()
    {
        GoodWorkspace();
        Write("preflight.base.json", """{ "schemaVersion": 1, "historyPath": "build/history" }""");

        Invoke("run", "--stage", "workspace").ShouldBe(0);

        Directory
            .EnumerateFiles(Path.Combine(_workspace.FullName, "build", "history"), "*.ndjson")
            .ShouldHaveSingleItem();
    }

    /// <remarks>
    /// <c>per-process</c> is the escape for the row the history format's table marks
    /// as having no guarantee at all.
    /// </remarks>
    [Fact]
    public void Run_UnderPerProcessMode_NamesTheFileAfterTheProcess()
    {
        GoodWorkspace();
        Write("preflight.base.json", """{ "schemaVersion": 1, "historyMode": "per-process" }""");

        Invoke("run", "--stage", "workspace").ShouldBe(0);

        Directory
            .EnumerateFiles(HistoryDirectory(), "*.ndjson")
            .Select(Path.GetFileName)
            .ShouldBe(["2026-08.WKS-1234.4242.ndjson"]);
    }

    /// <summary>
    /// A run recorded here is readable by the command that reads it back.
    /// </summary>
    /// <remarks>
    /// The one assertion neither side can make alone. A writer and a reader that
    /// each pass their own tests and disagree about the shape between them is the
    /// defect this catches, and the instrumentation has no value at all if it happens.
    /// </remarks>
    [Fact]
    public void Run_ThenReport_ReadsTheRunBack()
    {
        GoodWorkspace();

        Invoke("run", "--stage", "workspace").ShouldBe(0);

        _output.GetStringBuilder().Clear();

        Invoke("report", "--since", "30d").ShouldBe(0);

        var text = _output.ToString();

        text.ShouldContain("Runs");
        text.ShouldNotContain("Nothing recorded");
        text.ShouldNotContain("could not be read");
    }

    private string HistoryDirectory() => Path.Combine(_workspace.FullName, ".preflight", "history");

    private IReadOnlyList<string> HistoryLines() =>
    [
        .. Directory
            .EnumerateFiles(HistoryDirectory(), "*.ndjson")
            .Order(StringComparer.Ordinal)
            .SelectMany(File.ReadAllLines),
    ];

    /// <remarks>
    /// The toolchain rule is the only root of the workspace stage, and git is
    /// the one tool every machine that can build this project has.
    /// </remarks>
    private void GoodWorkspace() => Write("preflight.workspace.json", """
        {
          "tools": [
            { "name": "git", "command": "git", "arguments": ["--version"], "minimumVersion": "2.0.0" }
          ]
        }
        """);

    private void Write(string relativePath, string content) =>
        WorkspaceFiles.Write(_workspace, relativePath, content);

    private int Invoke(params string[] args) => Invoke(null, args);

    private int Invoke(Preflight.Core.History.IHistoryStore? history, params string[] args) =>
        PreflightCommandLine.Execute(
            args,
            _output,
            _error,
            parse => CommandDispatcher.Run(
                parse,
                CommandEnvironments.For(_workspace, _output, _error, _clock, history: history)));
}
