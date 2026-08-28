namespace Preflight.Rules.Tests.Build;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="CompileProbeRule"/> without invoking a compiler.
/// </summary>
/// <remarks>
/// This rule is the reason <see cref="IProcessRunner"/> exists: it is precisely
/// the one that most needs to be testable without invoking a real compiler.
/// Every test here is that claim being cashed in.
/// </remarks>
public sealed class CompileProbeRuleTests
{
    private const string Manifest = """
        {
          "compileProbe": {
            "command": "dotnet",
            "arguments": ["build", "-t:Compile", "-p:OutputPath={probeOutput}"],
            "workingDirectory": "src"
          }
        }
        """;

    private readonly CompileProbeRule _rule = new();

    private static IFileSystem ManifestContaining(string? json)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(json is not null);

        if (json is not null)
        {
            fileSystem.ReadAllTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(json);
        }

        return fileSystem;
    }

    private static IProcessRunner RunnerReturning(
        int exitCode,
        string standardOutput = "",
        string standardError = "")
    {
        var runner = Substitute.For<IProcessRunner>();

        runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(exitCode, standardOutput, standardError, TimeSpan.FromSeconds(4)));

        return runner;
    }

    private Task<RuleOutcome> Run(IProcessRunner processes, string? manifest = Manifest) =>
        _rule.ExecuteAsync(
            Context(
                fileSystem: ManifestContaining(manifest),
                processes: processes,
                stage: ValidationStage.BuildReadiness),
            CancellationToken.None);

    [Fact]
    public async Task ExecuteAsync_WhenTheProbeSucceeds_Passes()
    {
        (await Run(RunnerReturning(0))).Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoProbeDeclared_IsNotApplicable()
    {
        (await Run(RunnerReturning(0), """{ "tools": [] }""")).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoManifest_IsNotApplicable()
    {
        (await Run(RunnerReturning(0), manifest: null)).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithAMalformedManifest_Fails()
    {
        (await Run(RunnerReturning(0), "{ not json")).Status.ShouldBe(RuleStatus.Failed);
    }

    /// <summary>
    /// The rule runs what the manifest declared, and nothing else.
    /// </summary>
    /// <remarks>
    /// The probe compiles without linking, and nothing here can enforce that —
    /// the flag that means it differs per compiler. What the rule can do is
    /// pass the declared arguments through untouched, so a manifest that asked
    /// for a compile-only build gets one. A rule that appended arguments of its
    /// own would be guessing at flag syntax, and guessing wrong turns a probe
    /// into a full build.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_RunsExactlyWhatTheManifestDeclared()
    {
        var runner = RunnerReturning(0);

        await Run(runner);

        await runner.Received(1).RunAsync(
            Arg.Is<ProcessRequest>(request =>
                request.FileName == "dotnet" &&
                request.Arguments.Contains("build") &&
                request.Arguments.Contains("-t:Compile") &&
                request.WorkingDirectory!.EndsWith("src", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The output token is substituted with a path outside the workspace.
    /// </summary>
    /// <remarks>
    /// The tool never writes to the workspace, and a compiler told nothing
    /// writes its intermediates next to the sources. The read-only
    /// <see cref="IFileSystem"/> cannot prevent that — the rule does not do the
    /// writing, the child does. This token is the only mechanism that can.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_SubstitutesTheOutputTokenWithAPathOutsideTheWorkspace()
    {
        var runner = RunnerReturning(0);

        await Run(runner);

        await runner.Received(1).RunAsync(
            Arg.Is<ProcessRequest>(request =>
                request.Arguments.All(argument => !argument.Contains(CompileProbeRule.OutputToken)) &&
                request.Arguments.Any(argument =>
                    argument.Contains("preflight-probe") &&
                    !argument.Contains(WorkspaceRoot.FullName))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithNoWorkingDirectoryDeclared_RunsAtTheWorkspaceRoot()
    {
        var runner = RunnerReturning(0);

        await Run(runner, """{ "compileProbe": { "command": "dotnet", "arguments": ["build"] } }""");

        await runner.Received(1).RunAsync(
            Arg.Is<ProcessRequest>(request => request.WorkingDirectory == WorkspaceRoot.FullName),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// One finding per diagnostic, with the file and the line.
    /// </summary>
    /// <remarks>
    /// A failing probe that reported one blob of text would put the compiler's
    /// entire output on one line of the report and give the reader nothing to
    /// click.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheProbeFails_EmitsAFindingPerDiagnostic()
    {
        var output = string.Join(
            '\n',
            "src/Game/Player.cs(42,13): error CS1002: ; expected",
            "src/Game/Enemy.cs(87): error CS0103: the name 'foo' does not exist");

        var outcome = await Run(RunnerReturning(1, output));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.Count.ShouldBe(2);

        outcome.Findings[0].Location!.RelativePath.ShouldBe("src/Game/Player.cs");
        outcome.Findings[0].Location!.Line.ShouldBe(42);
        outcome.Findings[0].Location!.Column.ShouldBe(13);
        outcome.Findings[0].Message.ShouldContain("CS1002");

        outcome.Findings[1].Location!.Line.ShouldBe(87);
        outcome.Findings[1].Location!.Column.ShouldBeNull();
    }

    /// <remarks>
    /// MSBuild and the C# compiler write one form, clang and gcc another.
    /// Supporting a single form would silently drop every diagnostic from half
    /// the toolchains a production might use.
    /// </remarks>
    [Theory]
    [InlineData("src/main.cpp:42:13: error: expected ';'", "src/main.cpp", 42, 13)]
    [InlineData("src/main.cpp:42: error: expected ';'", "src/main.cpp", 42, null)]
    [InlineData(@"src\Game\Player.cs(7,1): error CS1519: invalid token", "src/Game/Player.cs", 7, 1)]
    [InlineData("src/main.cpp:9:1: fatal error: header.h: No such file", "src/main.cpp", 9, 1)]
    public async Task ExecuteAsync_ReadsBothDiagnosticForms(
        string line,
        string expectedPath,
        int expectedLine,
        int? expectedColumn)
    {
        var outcome = await Run(RunnerReturning(1, line));

        var finding = outcome.Findings.ShouldHaveSingleItem();

        finding.Location!.RelativePath.ShouldBe(expectedPath);
        finding.Location.Line.ShouldBe(expectedLine);
        finding.Location.Column.ShouldBe(expectedColumn);
    }

    /// <remarks>
    /// MSBuild writes diagnostics to standard output, clang to standard error.
    /// Reading one stream loses the other's entirely.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_ReadsDiagnosticsFromBothStreams()
    {
        var outcome = await Run(RunnerReturning(
            1,
            standardOutput: "a.cs(1,1): error CS0001: from stdout",
            standardError: "b.cpp:2:2: error: from stderr"));

        outcome.Findings.Count.ShouldBe(2);
    }

    /// <summary>
    /// A failure with no recognisable diagnostic still carries evidence.
    /// </summary>
    /// <remarks>
    /// A failing rule that says nothing is the one thing it must not be. A
    /// probe can fail for reasons no diagnostic parser will recognise — a
    /// missing project file, a licence check — and reporting zero findings
    /// would leave the reader a red line and nothing else.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheProbeFailsWithNothingParseable_StillReportsEvidence()
    {
        var outcome = await Run(RunnerReturning(1, standardError: "MSB1003: Specify a project or solution file."));

        outcome.Status.ShouldBe(RuleStatus.Failed);

        var finding = outcome.Findings.ShouldHaveSingleItem();

        finding.Actual.ShouldNotBeNull().ShouldContain("MSB1003");
        finding.Remediation.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAsync_WithVeryLongUnparseableOutput_TruncatesIt()
    {
        var outcome = await Run(RunnerReturning(1, standardError: new string('x', 10_000)));

        outcome.Findings.ShouldHaveSingleItem().Actual.ShouldNotBeNull().Length.ShouldBeLessThan(600);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheProbeFailsSilently_FallsBackToStandardOutput()
    {
        var outcome = await Run(RunnerReturning(1, standardOutput: "something went wrong"));

        outcome.Findings.ShouldHaveSingleItem().Actual.ShouldNotBeNull().ShouldContain("something went wrong");
    }

    [Fact]
    public async Task ExecuteAsync_PassesTheCancellationTokenToTheProbe()
    {
        var runner = RunnerReturning(0);
        using var cancellation = new CancellationTokenSource();

        await _rule.ExecuteAsync(
            Context(
                fileSystem: ManifestContaining(Manifest),
                processes: runner,
                stage: ValidationStage.BuildReadiness),
            cancellation.Token);

        await runner.Received(1).RunAsync(Arg.Any<ProcessRequest>(), cancellation.Token);
    }

    /// <summary>
    /// A timeout is the engine's verdict, not the rule's.
    /// </summary>
    /// <remarks>
    /// This is the rule most likely to hit the timeout, and the one where
    /// swallowing it does the most damage: <c>Failed</c> would say the
    /// workspace does not compile, when what happened is that the probe ran out
    /// of time.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheProbeIsCancelled_DoesNotSwallowIt()
    {
        var runner = Substitute.For<IProcessRunner>();

        runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProcessResult>>(_ => throw new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => Run(runner));
    }

    [Fact]
    public void Diagnostics_WithACancelledToken_StopsRatherThanFinishing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var output = string.Join('\n', Enumerable.Repeat("a.cs(1,1): error CS0001: x", 500));

        Should.Throw<OperationCanceledException>(() =>
            CompileProbeRule.Diagnostics(
                new ProcessResult(1, output, string.Empty, TimeSpan.Zero),
                cancellation.Token));
    }
}
