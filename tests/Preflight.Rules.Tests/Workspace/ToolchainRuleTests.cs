namespace Preflight.Rules.Tests.Workspace;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="ToolchainRule"/>.
/// </summary>
/// <remarks>
/// No tool is installed for these and none is invoked. The rule reaches every
/// executable through <see cref="IProcessRunner"/>, so a substitute can hand it
/// the exact banner a preview SDK or a Windows build of git prints — output
/// this suite could not otherwise produce on the one machine it runs on, and
/// output that is precisely where the version parsing goes wrong.
/// </remarks>
public sealed class ToolchainRuleTests
{
    private const string ManifestPath = "preflight.workspace.json";

    private readonly ToolchainRule _rule = new();

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

    private static IProcessRunner RunnerPrinting(string output, int exitCode = 0, string standardError = "")
    {
        var runner = Substitute.For<IProcessRunner>();

        runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(exitCode, output, standardError, TimeSpan.FromMilliseconds(30)));

        return runner;
    }

    private static string ManifestFor(string? minimum = "10.0.0", string? maximum = "11.0.0")
    {
        var bounds = new List<string>();

        if (minimum is not null)
        {
            bounds.Add($"\"minimumVersion\": \"{minimum}\"");
        }

        if (maximum is not null)
        {
            bounds.Add($"\"maximumVersion\": \"{maximum}\"");
        }

        return $$"""
            {
              "tools": [
                {
                  "name": ".NET SDK",
                  "command": "dotnet",
                  "arguments": ["--version"]{{(bounds.Count > 0 ? ", " + string.Join(", ", bounds) : string.Empty)}}
                }
              ]
            }
            """;
    }

    private Task<RuleOutcome> Run(IFileSystem fileSystem, IProcessRunner processes) =>
        _rule.ExecuteAsync(
            Context(fileSystem: fileSystem, processes: processes, stage: ValidationStage.Workspace),
            CancellationToken.None);

    [Fact]
    public async Task ExecuteAsync_WhenTheToolIsPresentAndInRange_Passes()
    {
        var outcome = await Run(ManifestContaining(ManifestFor()), RunnerPrinting("10.0.100"));

        outcome.Status.ShouldBe(RuleStatus.Passed);
        outcome.Findings.ShouldBeEmpty();
    }

    /// <summary>
    /// A missing manifest fails; it does not report n/a.
    /// </summary>
    /// <remarks>
    /// The trapdoor this rule is most likely to fall through. <c>n/a</c> for a
    /// path that does not resolve makes a mistyped <c>manifestPath</c> green
    /// forever — and a permanently green rule is worse than an absent one,
    /// because it is counted as evidence.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithNoManifest_Fails()
    {
        var outcome = await Run(ManifestContaining(null), RunnerPrinting("10.0.100"));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Message.ShouldContain("missing");
    }

    /// <remarks>
    /// A manifest that is present and declares no tools is the other fact:
    /// somebody said in writing that there is nothing to check.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAManifestDeclaringNoTools_IsNotApplicable()
    {
        var outcome = await Run(ManifestContaining("""{ "tools": [] }"""), RunnerPrinting("10.0.100"));

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithAMalformedManifest_FailsNamingTheFile()
    {
        var outcome = await Run(ManifestContaining("{ not json"), RunnerPrinting("10.0.100"));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Location!.RelativePath.ShouldEndWith(ManifestPath);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheToolIsNotInstalled_FailsNamingTheCommand()
    {
        var runner = Substitute.For<IProcessRunner>();

        runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProcessResult>>(_ => throw new InvalidOperationException("no such executable"));

        var outcome = await Run(ManifestContaining(ManifestFor()), runner);

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Expected.ShouldNotBeNull().ShouldContain("dotnet");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheToolExitsNonZero_Fails()
    {
        var outcome = await Run(
            ManifestContaining(ManifestFor()),
            RunnerPrinting(string.Empty, exitCode: 127, standardError: "command not found"));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Actual.ShouldNotBeNull().ShouldContain("command not found");
    }

    /// <remarks>
    /// A tool that fails and says nothing is common — a shell reporting
    /// "command not found" on stdout, or a launcher that exits silently. An
    /// empty <c>Actual</c> would render as a label with nothing after it, which
    /// reads as the report being broken rather than the tool being absent.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheToolFailsSilently_StillSaysSomething()
    {
        var outcome = await Run(
            ManifestContaining(ManifestFor()),
            RunnerPrinting(string.Empty, exitCode: 1, standardError: string.Empty));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Actual.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The range boundaries, all five of them.
    /// </summary>
    /// <remarks>
    /// The upper bound is exclusive: "anything in 10.x" is written 10.0.0 to
    /// 11.0.0, and an inclusive ceiling would need a version nobody can write
    /// down. Both ends are tested at the boundary rather than near it, because
    /// near it is where an off-by-one is invisible.
    /// </remarks>
    [Theory]
    [InlineData("9.0.400", false)]
    [InlineData("10.0.0", true)]
    [InlineData("10.5.200", true)]
    [InlineData("11.0.0", false)]
    [InlineData("12.0.0", false)]
    public async Task ExecuteAsync_AppliesTheRangeAtItsBoundaries(string version, bool accepted)
    {
        var outcome = await Run(ManifestContaining(ManifestFor()), RunnerPrinting(version));

        outcome.Status.ShouldBe(accepted ? RuleStatus.Passed : RuleStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOutOfRange_ReportsBothTheRangeAndTheVersionFound()
    {
        var outcome = await Run(ManifestContaining(ManifestFor()), RunnerPrinting("9.0.400"));

        var finding = outcome.Findings.ShouldHaveSingleItem();

        finding.Expected.ShouldNotBeNull().ShouldContain("10.0.0");
        finding.Expected.ShouldNotBeNull().ShouldContain("11.0.0");
        finding.Actual.ShouldNotBeNull().ShouldContain("9.0.400");
        finding.Remediation.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(null, "11.0.0", "9.0.0", true)]
    [InlineData("10.0.0", null, "99.0.0", true)]
    [InlineData(null, null, "1.0.0", true)]
    [InlineData(null, "11.0.0", "11.0.0", false)]
    [InlineData("10.0.0", null, "9.9.9", false)]
    public async Task ExecuteAsync_WithAnOpenEndedRange_OnlyAppliesTheBoundGiven(
        string? minimum,
        string? maximum,
        string version,
        bool accepted)
    {
        var outcome = await Run(ManifestContaining(ManifestFor(minimum, maximum)), RunnerPrinting(version));

        outcome.Status.ShouldBe(accepted ? RuleStatus.Passed : RuleStatus.Failed);
    }

    /// <summary>
    /// A prerelease SDK is still that SDK.
    /// </summary>
    /// <remarks>
    /// <c>dotnet --version</c> prints <c>10.0.100-preview.3.25</c> on a preview
    /// install. A parser that refused it would report a machine that has the
    /// SDK as having none — and the developer running previews is usually the
    /// one least able to explain why the tool disagrees with them.
    /// </remarks>
    [Theory]
    [InlineData("10.0.100-preview.3.25")]
    [InlineData("10.0.100\n")]
    [InlineData("dotnet 10.0.100")]
    // git on Windows prints five components, the last two of which are not
    // numbers. This is the case that forces ParseVersion to take the leading
    // numeric run rather than the whole token.
    [InlineData("git version 10.0.100.windows.1")]
    [InlineData("10.0.100.5")]
    public async Task ExecuteAsync_ReadsTheVersionOutOfRealToolOutput(string output)
    {
        (await Run(ManifestContaining(ManifestFor()), RunnerPrinting(output))).Status.ShouldBe(RuleStatus.Passed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no version here")]
    [InlineData("   ")]
    public async Task ExecuteAsync_WhenTheOutputCarriesNoVersion_FailsSayingSo(string output)
    {
        var outcome = await Run(ManifestContaining(ManifestFor()), RunnerPrinting(output));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Message.ShouldContain("version");
    }

    /// <summary>
    /// A token with more than four components keeps the first four.
    /// </summary>
    /// <remarks>
    /// <see cref="Version"/> holds four, and keeping the leading four is what
    /// makes <c>2.51.0.windows.5</c> readable at all. The alternative —
    /// refusing anything that is not exactly a version — reports a machine that
    /// has git installed as having none, and the developer it happens to has no
    /// way to tell that from the tool genuinely being absent.
    /// </remarks>
    [Fact]
    public void ParseVersion_WithMoreComponentsThanVersionHolds_KeepsTheLeadingFour()
    {
        ToolchainRule.ParseVersion("1.2.3.4.5").ShouldBe(new Version(1, 2, 3, 4));
    }

    /// <remarks>
    /// A bare integer is not a version:
    /// <see cref="Version.TryParse(string, out Version)"/> needs at least a
    /// major and a minor, and a tool printing "1" is not telling this rule what
    /// it needs to compare.
    /// </remarks>
    [Theory]
    [InlineData("no digits at all")]
    [InlineData("1")]
    [InlineData("")]
    public void ParseVersion_WithNothingComparable_ReturnsNull(string output)
    {
        ToolchainRule.ParseVersion(output).ShouldBeNull();
    }

    /// <summary>
    /// A bound the manifest author mistyped is ignored, not obeyed.
    /// </summary>
    /// <remarks>
    /// The alternative is worse in both directions. Treating an unparseable
    /// bound as zero would accept everything and make the rule silently
    /// toothless; treating it as infinity would reject every version and make
    /// the rule impossible to satisfy. Ignoring it leaves the other bound
    /// working, which is the closest thing to what the author meant.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAnUnparseableBound_IgnoresItAndKeepsTheOther()
    {
        var manifest = ManifestFor(minimum: "not-a-version", maximum: "11.0.0");

        (await Run(ManifestContaining(manifest), RunnerPrinting("1.0.0"))).Status.ShouldBe(RuleStatus.Passed);
        (await Run(ManifestContaining(manifest), RunnerPrinting("11.0.0"))).Status.ShouldBe(RuleStatus.Failed);
    }

    /// <remarks>
    /// This text is whatever the tool chose to print, and it is rendered in the
    /// console report and stored in the run's history, where one record is
    /// capped at 64 KB. A tool that answers a bad argument with its entire help
    /// would fill the terminal and cost the history record the fields that come
    /// after this one.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithVeryLongToolOutput_TruncatesItInTheFinding()
    {
        var outcome = await Run(
            ManifestContaining(ManifestFor()),
            RunnerPrinting(string.Empty, exitCode: 1, standardError: new string('x', 5_000)));

        outcome.Findings.ShouldHaveSingleItem().Actual.ShouldNotBeNull().Length.ShouldBeLessThan(300);
    }

    /// <remarks>
    /// A manifest whose entire content is the JSON literal <c>null</c> parses
    /// successfully to nothing. Treated as an empty manifest rather than as a
    /// missing one: the file is there, so the path is right, and reporting it
    /// missing would send someone looking for a file they are staring at.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAManifestThatIsJsonNull_IsNotApplicable()
    {
        (await Run(ManifestContaining("null"), RunnerPrinting("10.0.100")))
            .Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_RunsTheToolInTheWorkspaceRoot()
    {
        var runner = RunnerPrinting("10.0.100");

        await Run(ManifestContaining(ManifestFor()), runner);

        await runner.Received(1).RunAsync(
            Arg.Is<ProcessRequest>(request =>
                request.FileName == "dotnet" &&
                request.Arguments.Contains("--version") &&
                request.WorkingDirectory == WorkspaceRoot.FullName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PassesTheCancellationTokenToTheTool()
    {
        var runner = RunnerPrinting("10.0.100");
        using var cancellation = new CancellationTokenSource();

        await _rule.ExecuteAsync(
            Context(fileSystem: ManifestContaining(ManifestFor()), processes: runner, stage: ValidationStage.Workspace),
            cancellation.Token);

        await runner.Received(1).RunAsync(Arg.Any<ProcessRequest>(), cancellation.Token);
    }

    /// <summary>
    /// A timeout is the tool's verdict, not the rule's.
    /// </summary>
    /// <remarks>
    /// A timeout is <c>Errored</c> — a defect in the rule or the environment. A
    /// rule that caught its own cancellation and reported <c>Failed</c> would
    /// blame the workspace for its own deadline, and the report would name the
    /// wrong thing to fix.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheToolIsCancelled_DoesNotSwallowIt()
    {
        var runner = Substitute.For<IProcessRunner>();

        runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProcessResult>>(_ => throw new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() =>
            Run(ManifestContaining(ManifestFor()), runner));
    }

    [Fact]
    public async Task ExecuteAsync_WithACancelledToken_StopsBeforeRunningAnything()
    {
        var runner = RunnerPrinting("10.0.100");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _rule.ExecuteAsync(
                Context(
                    fileSystem: ManifestContaining(ManifestFor()),
                    processes: runner,
                    stage: ValidationStage.Workspace),
                cancellation.Token));
    }

    [Fact]
    public async Task ExecuteAsync_ReadsTheManifestPathFromPolicy()
    {
        var fileSystem = ManifestContaining(ManifestFor());

        await _rule.ExecuteAsync(
            Context(
                policy: PolicyWith("manifestPath", "config/tools.json"),
                fileSystem: fileSystem,
                processes: RunnerPrinting("10.0.100"),
                stage: ValidationStage.Workspace),
            CancellationToken.None);

        fileSystem.Received().FileExists(Arg.Is<string>(path => path.EndsWith("tools.json", StringComparison.Ordinal)));
    }
}
