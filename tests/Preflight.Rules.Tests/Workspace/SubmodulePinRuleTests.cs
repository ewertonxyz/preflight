namespace Preflight.Rules.Tests.Workspace;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="SubmodulePinRule"/>, and with it the reader that turns one
/// line of client output into a state.
/// </summary>
/// <remarks>
/// The reader has no test file of its own, for the reason the other
/// collaborators do not: it is internal, this repository has no
/// <c>InternalsVisibleTo</c> by decision, and the rule is the only place its
/// behaviour is observable to anyone.
/// </remarks>
public sealed class SubmodulePinRuleTests
{
    private readonly SubmodulePinRule _rule = new();

    private const string Object = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task ExecuteAsync_WithNoSubmodules_ReportsNotApplicable()
    {
        var outcome = await Run(Printing(string.Empty));

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
        outcome.Findings.ShouldBeEmpty();
    }

    /// <summary>
    /// Output that holds no submodule, however it is terminated.
    /// </summary>
    /// <remarks>
    /// Never passed: a tick over output that held nothing would claim
    /// submodules were checked when none were named. The second row reaches
    /// further than the first — a line of nothing but a carriage return
    /// survives the split and becomes empty only once its terminator is
    /// trimmed, so it is the reader rather than the split that has to answer
    /// for it.
    /// </remarks>
    [Theory]
    [InlineData("\n\n")]
    [InlineData("\r\n\r\n")]
    public async Task ExecuteAsync_WithOnlyBlankLines_ReportsNotApplicable(string output) =>
        (await Run(Printing(output))).Status.ShouldBe(RuleStatus.NotApplicable);

    /// <summary>
    /// A workspace that is not a checkout is not this rule's problem.
    /// </summary>
    /// <remarks>
    /// The test that keeps the rule from blocking every checkout that is not
    /// under this client — an exported archive, a checkout under another
    /// client, and every temporary directory the specification scenarios build.
    /// Nobody configured this rule; it runs everywhere, so it has to be silent
    /// where there is nothing to say.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_InAWorkspaceThatIsNotARepository_ReportsNotApplicable()
    {
        var outcome = await RunWith(
            Answering(new ProcessResult(128, string.Empty, "fatal: not a git repository", TimeSpan.Zero)));

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheClientIsNotOnThePath_ReportsNotApplicable() =>
        (await RunWith(Throwing())).Status.ShouldBe(RuleStatus.NotApplicable);

    /// <remarks>
    /// A client that recognises the workspace and then fails the question is a
    /// different fact from a workspace that is not a checkout, and the reader
    /// needs the exit code to find out which command to run by hand.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheStatusCommandFails_ReportsFailedNamingTheCommandAndTheCode()
    {
        var outcome = await Run(new ProcessResult(1, string.Empty, "something else", TimeSpan.Zero));

        outcome.Status.ShouldBe(RuleStatus.Failed);

        var finding = outcome.Findings.Single();

        finding.Message.ShouldContain("git submodule status");
        finding.Actual.ShouldBe("exit code 1");
    }

    /// <summary>
    /// The client's alphabet, one row per character.
    /// </summary>
    /// <remarks>
    /// The leading space is the whole reason the collaborator is handed the
    /// line untrimmed: trim it and every healthy submodule becomes one the tool
    /// cannot read.
    /// </remarks>
    [Theory]
    [InlineData(' ', RuleStatus.Passed)]
    [InlineData('-', RuleStatus.Warning)]
    [InlineData('+', RuleStatus.Failed)]
    [InlineData('U', RuleStatus.Failed)]
    [InlineData('?', RuleStatus.Failed)]
    public async Task ExecuteAsync_ForEachStatusCharacter_ReportsTheExpectedOutcome(char state, RuleStatus status) =>
        (await Run(Printing($"{state}{Object} ext/lib (v1.0)\n"))).Status.ShouldBe(status);

    /// <summary>
    /// A recoverable state and a blocking one together are a failure.
    /// </summary>
    /// <remarks>
    /// The row that matters is the last. Reporting the pair as a warning would
    /// hide a failure behind an exit code of zero, which is the silent
    /// downgrade the two-degree distinction exists to prevent.
    /// </remarks>
    [Theory]
    [InlineData("", RuleStatus.NotApplicable)]
    [InlineData("-", RuleStatus.Warning)]
    [InlineData("+", RuleStatus.Failed)]
    [InlineData("-+", RuleStatus.Failed)]
    public async Task ExecuteAsync_ForEachCombinationOfStates_FoldsToTheExpectedStatus(
        string states,
        RuleStatus status)
    {
        var output = string.Concat(states.Select((state, index) => $"{state}{Object} ext/lib{index}\n"));

        (await Run(Printing(output))).Status.ShouldBe(status);
    }

    /// <remarks>
    /// The order of the list is the only hierarchy a rule has, and a warning
    /// printed above a failure reads as the more important of the two.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAWarningAndAFailure_OrdersTheFailingFindingFirst()
    {
        var outcome = await Run(Printing($"-{Object} ext/a\n+{Object} ext/b\n"));

        outcome.Findings[0].Location!.RelativePath.ShouldBe("ext/b");
        outcome.Findings[1].Location!.RelativePath.ShouldBe("ext/a");
    }

    /// <remarks>
    /// Reported rather than skipped. A line quietly ignored is a submodule
    /// nobody checked inside a run that reported success.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAnUnrecognisedLeadingCharacter_ReportsFailedQuotingTheLine()
    {
        var outcome = await Run(Printing($"?{Object} ext/lib\n"));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.Single().Actual!.ShouldContain("ext/lib");
    }

    /// <remarks>
    /// A command, and nobody has to decide anything, which is the whole of what
    /// separates this state from the two that fail.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_ForAnUninitialisedSubmodule_RemediatesWithTheInitCommand()
    {
        var outcome = await Run(Printing($"-{Object} ext/lib\n"));

        outcome.Findings.Single().Remediation!.ShouldContain("submodule update --init");
    }

    /// <summary>
    /// The shapes one line of client output takes.
    /// </summary>
    /// <remarks>
    /// A path with a space in it is the row that decides the implementation. A
    /// content repository has directories with spaces, and a reader that split
    /// on whitespace would report a submodule at a path that does not exist.
    /// </remarks>
    [Theory]
    [InlineData("ext/lib", "")]
    [InlineData("ext/lib", " (v1.0)")]
    [InlineData("ext/nested/deep/lib", " (heads/main)")]
    [InlineData("ext/third party/lib", " (v2)")]
    [InlineData("ext/third party/lib", "")]
    public async Task ExecuteAsync_ForEachLineShape_ReportsThePathItNamed(string path, string describe)
    {
        var outcome = await Run(Printing($"+{Object} {path}{describe}\n"));

        outcome.Findings.Single().Location!.RelativePath.ShouldBe(path);
    }

    /// <remarks>
    /// A line with nothing after the object name is not one the client writes,
    /// and the reader still has to answer rather than step off the end of it.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithALineCarryingNoPath_StillReportsIt()
    {
        var outcome = await Run(Printing($"+{Object}\n"));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.Single().Location!.RelativePath.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithCarriageReturnsInTheOutput_ReadsTheLinesAnyway() =>
        (await Run(Printing($" {Object} ext/lib\r\n"))).Status.ShouldBe(RuleStatus.Passed);

    [Fact]
    public async Task ExecuteAsync_WithNoManifest_UsesTheDefaultClient()
    {
        var requests = new List<ProcessRequest>();

        await RunWith(Recording(requests, Printing(string.Empty)), manifest: null);

        requests.ShouldAllBe(request => request.FileName == "git");
    }

    [Fact]
    public async Task ExecuteAsync_WithADeclaredClient_UsesIt()
    {
        var requests = new List<ProcessRequest>();

        await RunWith(
            Recording(requests, Printing(string.Empty)),
            manifest: """{ "tools": [], "vcs": { "command": "hg" } }""");

        requests.ShouldAllBe(request => request.FileName == "hg");
    }

    /// <summary>
    /// A manifest that will not parse does not stop this rule.
    /// </summary>
    /// <remarks>
    /// The rule reads that file for the client's name and for nothing else, so
    /// it falls back to its default and carries on. A submodule pointing at the
    /// wrong commit is worth reporting whatever state the manifest is in, and
    /// the rules that do read something out of it already say the file is
    /// broken.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAMalformedManifest_StillChecksTheSubmodules()
    {
        var outcome = await RunWith(
            Repository(Printing($"+{Object} ext/lib\n")),
            manifest: "{ nope");

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.Single().Message.ShouldContain("submodule");
    }

    [Fact]
    public async Task ExecuteAsync_RunsTheClientInTheWorkspaceRoot()
    {
        var requests = new List<ProcessRequest>();

        await RunWith(Recording(requests, Printing(string.Empty)), manifest: null);

        requests.ShouldAllBe(request => request.WorkingDirectory == WorkspaceRoot.FullName);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_DoesNotSwallowTheCancellation()
    {
        using var source = new CancellationTokenSource();

        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _rule.ExecuteAsync(
                Context(
                    fileSystem: Workspace(null),
                    processes: Cancelling(),
                    stage: ValidationStage.Workspace),
                source.Token));
    }

    private Task<RuleOutcome> Run(ProcessResult status) => RunWith(Repository(status), manifest: null);

    private Task<RuleOutcome> RunWith(IProcessRunner processes, string? manifest = null) =>
        _rule.ExecuteAsync(
            Context(
                fileSystem: Workspace(manifest),
                processes: processes,
                stage: ValidationStage.Workspace),
            CancellationToken.None);

    private static IFileSystem Workspace(string? manifest)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(manifest is not null);

        if (manifest is not null)
        {
            fileSystem.ReadAllTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(manifest);
        }

        return fileSystem;
    }

    /// <summary>
    /// A client that recognises the workspace and then answers the status
    /// question.
    /// </summary>
    /// <remarks>
    /// Dispatched on the arguments rather than on the order of calls, because
    /// the rule asks two different questions and a blind substitute would
    /// answer the first with what was meant for the second.
    /// </remarks>
    private static IProcessRunner Repository(ProcessResult status)
    {
        var processes = Substitute.For<IProcessRunner>();

        processes.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ProcessRequest>().Arguments[0] == "rev-parse"
                ? Printing(".git")
                : status);

        return processes;
    }

    private static IProcessRunner Recording(List<ProcessRequest> requests, ProcessResult status)
    {
        var processes = Substitute.For<IProcessRunner>();

        processes.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();

                requests.Add(request);

                return request.Arguments[0] == "rev-parse" ? Printing(".git") : status;
            });

        return processes;
    }

    private static IProcessRunner Answering(ProcessResult result)
    {
        var processes = Substitute.For<IProcessRunner>();

        processes.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>()).Returns(result);

        return processes;
    }

    private static IProcessRunner Throwing()
    {
        var processes = Substitute.For<IProcessRunner>();

        processes.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProcessResult>>(_ => throw new InvalidOperationException("no such executable"));

        return processes;
    }

    private static IProcessRunner Cancelling()
    {
        var processes = Substitute.For<IProcessRunner>();

        processes.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProcessResult>>(call => throw new OperationCanceledException(call.Arg<CancellationToken>()));

        return processes;
    }

    private static ProcessResult Printing(string standardOutput) =>
        new(0, standardOutput, string.Empty, TimeSpan.Zero);
}
