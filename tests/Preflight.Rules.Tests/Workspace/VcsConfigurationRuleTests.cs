namespace Preflight.Rules.Tests.Workspace;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="VcsConfigurationRule"/>, and with it the collaborator that
/// invokes a version control client.
/// </summary>
/// <remarks>
/// The collaborator has no test file of its own. It is internal, this
/// repository has no <c>InternalsVisibleTo</c> by decision, and a collaborator
/// is never surface — so every branch of it is driven through the rules that
/// hold it, which is also the only place its behaviour is observable to anyone.
/// </remarks>
public sealed class VcsConfigurationRuleTests
{
    private readonly VcsConfigurationRule _rule = new();

    private const string OneExpectedSetting = """
        { "tools": [], "vcs": { "settings": [{ "name": "core.longpaths", "expected": "true" }] } }
        """;

    private const string OnePresenceSetting = """
        { "tools": [], "vcs": { "settings": [{ "name": "filter.lfs.clean" }] } }
        """;

    [Fact]
    public async Task ExecuteAsync_WithNoManifest_ReportsNotApplicableWithoutRunningTheClient()
    {
        var processes = Substitute.For<IProcessRunner>();

        var outcome = await Run(null, processes: processes);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
        outcome.Findings.ShouldBeEmpty();

        await processes.DidNotReceive().RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    /// <remarks>
    /// Byte for byte the message the shared manifest reader produces. Three
    /// rules read that file, and a copy of the wording here would let one of
    /// them describe the same syntax error differently — leaving the reader to
    /// decide which of the two to believe.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithMalformedManifest_ReportsTheSharedSyntaxFinding()
    {
        var outcome = await Run("{ nope");

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.Single().Message.ShouldBe("The workspace manifest is not valid JSON.");
    }

    /// <remarks>
    /// The behaviour change this delivery introduces, and the one worth a test
    /// of its own: a key the manifest schema does not know used to be ignored,
    /// which left a rule reporting it checked nothing, forever, with no message
    /// anywhere saying why.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAMisspeltManifestKey_RefusesItRatherThanIgnoringIt()
    {
        var outcome = await Run("""{ "tools": [], "vcx": { "settings": [] } }""");

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.Single().Message.ShouldBe("The workspace manifest is not valid JSON.");
    }

    [Fact]
    public async Task ExecuteAsync_WithNoVcsKey_ReportsNotApplicable()
    {
        var processes = Substitute.For<IProcessRunner>();

        var outcome = await Run("""{ "tools": [] }""", processes: processes);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);

        await processes.DidNotReceive().RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    /// <remarks>
    /// A distinct arm from the key being absent. A workspace that wrote the key
    /// and left it empty declared nothing, and the rule has to say so rather
    /// than pass an empty loop.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAnEmptySettingsList_ReportsNotApplicable()
    {
        var outcome = await Run("""{ "tools": [], "vcs": { "command": "git", "settings": [] } }""");

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoCommandDeclared_RunsTheDefaultClient()
    {
        var requests = new List<ProcessRequest>();

        await Run(OneExpectedSetting, processes: Recording(requests, Printing("true")));

        requests.ShouldAllBe(request => request.FileName == "git");
    }

    [Fact]
    public async Task ExecuteAsync_WithADeclaredCommand_RunsIt()
    {
        var requests = new List<ProcessRequest>();

        await Run(
            """{ "tools": [], "vcs": { "command": "hg", "settings": [{ "name": "ui.tweakdefaults" }] } }""",
            processes: Recording(requests, Printing("on")));

        requests.ShouldAllBe(request => request.FileName == "hg");
    }

    /// <summary>
    /// The client is asked in the workspace, not wherever the tool started.
    /// </summary>
    /// <remarks>
    /// Without this the rule reads the configuration of whatever directory the
    /// process was launched from, which on a build agent is not the checkout
    /// being validated — and the verdict would be about somewhere else
    /// entirely, silently.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_RunsTheClientInTheWorkspaceRoot()
    {
        var requests = new List<ProcessRequest>();

        await Run(OneExpectedSetting, processes: Recording(requests, Printing("true")));

        requests.ShouldAllBe(request => request.WorkingDirectory == WorkspaceRoot.FullName);
    }

    /// <remarks>
    /// The whole comparison table in one theory, including the row that is only
    /// there because output is trimmed before it is compared.
    /// </remarks>
    [Theory]
    [InlineData(0, "true", "true", RuleStatus.Passed)]
    [InlineData(0, "true\n", "true", RuleStatus.Passed)]
    [InlineData(0, "  true  ", "true", RuleStatus.Passed)]
    [InlineData(0, "false", "true", RuleStatus.Failed)]
    [InlineData(0, "", "true", RuleStatus.Failed)]
    [InlineData(1, "", "true", RuleStatus.Failed)]
    [InlineData(0, "anything", null, RuleStatus.Passed)]
    [InlineData(0, "   ", null, RuleStatus.Failed)]
    [InlineData(1, "", null, RuleStatus.Failed)]
    public async Task ExecuteAsync_ForEachSettingOutcome_ReportsTheExpectedStatus(
        int exitCode,
        string standardOutput,
        string? expected,
        RuleStatus status)
    {
        var outcome = await Run(
            Manifest(expected),
            processes: Repository(new ProcessResult(exitCode, standardOutput, string.Empty, TimeSpan.Zero)));

        outcome.Status.ShouldBe(status);
    }

    /// <remarks>
    /// A third state the client has, and folding it into "not set" would send
    /// the reader looking for a setting that is in fact there twice.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAMultiValuedKey_ReportsAmbiguityAndNotAbsence()
    {
        var outcome = await Run(OneExpectedSetting, processes: Repository(Exit(2)));

        var finding = outcome.Findings.Single();

        finding.Message.ShouldContain("more than one value");
        finding.Remediation!.ShouldContain("--get-all");
    }

    [Fact]
    public async Task ExecuteAsync_WithTwoSettingsAndOneFailing_ReportsOneFindingForTheFailingOne()
    {
        const string Manifest = """
            {
              "tools": [],
              "vcs": {
                "settings": [
                  { "name": "core.bare", "expected": "false" },
                  { "name": "core.longpaths", "expected": "true" }
                ]
              }
            }
            """;

        var processes = Substitute.For<IProcessRunner>();

        processes.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ProcessRequest>().Arguments switch
            {
                ["rev-parse", ..] => Printing(".git"),
                [.., "core.bare"] => Printing("false"),
                _ => Printing("no"),
            });

        var outcome = await Run(Manifest, processes: processes);

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.Single().Message.ShouldContain("core.longpaths");
    }

    /// <summary>
    /// A machine without the client is a failed workspace, never an errored
    /// rule.
    /// </summary>
    /// <remarks>
    /// The tool accusing itself is the wrong answer here: what happened is that
    /// somebody has not installed a program, which is a fact about the machine.
    /// An errored rule sends the reader looking for a defect in the tool, and
    /// exits with the code that means the tool broke.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheClientIsNotOnThePath_ReportsFailedAndNotErrored()
    {
        var outcome = await Run(OneExpectedSetting, processes: Throwing());

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldNotBeEmpty();
    }

    /// <summary>
    /// A client that answers the first question and then cannot be started
    /// again.
    /// </summary>
    /// <remarks>
    /// Not a state anybody arranges on purpose, and it is reachable: the check
    /// that the workspace is a repository succeeds, and the launch of the next
    /// command fails. Reported as the setting not having been checked, which is
    /// what happened, rather than as the setting being absent.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheClientStopsRespondingAfterTheGate_SaysTheSettingWasNotChecked()
    {
        var processes = Substitute.For<IProcessRunner>();

        processes.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(Printing(".git")),
                _ => throw new InvalidOperationException("the client went away"));

        var outcome = await Run(OneExpectedSetting, processes: processes);

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.Single().Message.ShouldContain("could not be run");
    }

    /// <summary>
    /// A workspace that is not a checkout fails, because it declared that it
    /// would be one.
    /// </summary>
    /// <remarks>
    /// The deliberate asymmetry with the submodule rule, which answers "not
    /// applicable" to the same fact. Here the manifest declared a version
    /// control requirement, so a directory that cannot satisfy it is a
    /// workspace that is wrong.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_InAWorkspaceThatIsNotARepository_ReportsFailed()
    {
        var outcome = await Run(
            OneExpectedSetting,
            processes: Answering(new ProcessResult(128, string.Empty, "fatal: not a git repository", TimeSpan.Zero)));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.Single().Message.ShouldContain("not a 'git' checkout");
    }

    /// <summary>
    /// A presence check prints the setting's name and nothing it found.
    /// </summary>
    /// <remarks>
    /// The pair with the test below is what fixes the rule. A configuration
    /// value can be a personal address, a remote with a credential in it, or a
    /// path carrying an account name, and every field of a finding reaches a
    /// build log far more people read than ran the build. Whoever wrote an
    /// expected value asked for the comparison; nobody else did.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_ForAPresenceCheck_NeverEchoesWhatTheClientPrinted()
    {
        var outcome = await Run(
            OnePresenceSetting,
            processes: Repository(Printing("/home/someone/.gitignore")));

        outcome.Status.ShouldBe(RuleStatus.Passed);
        outcome.Findings.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ForAnExpectedCheck_FillsActualWithWhatItFound()
    {
        var outcome = await Run(OneExpectedSetting, processes: Repository(Printing("false")));

        outcome.Findings.Single().Actual.ShouldBe("false");
    }

    [Fact]
    public async Task ExecuteAsync_WithAFailingSetting_RemediatesInTheScopeItRead()
    {
        var outcome = await Run(OneExpectedSetting, processes: Repository(Printing("false")));

        outcome.Findings.Single().Remediation!.ShouldContain("git config --global core.longpaths");
    }

    /// <summary>
    /// A setting name the client would read as one of its own options.
    /// </summary>
    /// <remarks>
    /// An argument list keeps a shell from seeing the value; it does not keep
    /// the client from parsing a leading dash as a flag, and the configuration
    /// command has no separator that ends its options. Refused against the
    /// manifest, which is where the mistake is.
    /// </remarks>
    [Theory]
    [InlineData("--global")]
    [InlineData("")]
    public async Task ExecuteAsync_WithASettingNameTheClientWouldMisread_ReportsItAgainstTheManifest(string name)
    {
        var processes = Substitute.For<IProcessRunner>();

        processes.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Printing(".git")));

        var outcome = await Run(
            $$"""{ "tools": [], "vcs": { "settings": [{ "name": "{{name}}" }] } }""",
            processes: processes);

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.Single().Remediation!.ShouldContain("workspace manifest");
    }

    /// <remarks>
    /// A deadline that expired is the tool's verdict to give. A rule that
    /// swallowed the cancellation would report the workspace as broken when
    /// what happened is that a command ran long.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenCancelled_DoesNotSwallowTheCancellation()
    {
        using var source = new CancellationTokenSource();

        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _rule.ExecuteAsync(
                Context(
                    policy: EmptyPolicy(),
                    fileSystem: Workspace(OneExpectedSetting),
                    processes: Cancelling(),
                    stage: ValidationStage.Workspace),
                source.Token));
    }

    private static string Manifest(string? expected) => expected is null
        ? OnePresenceSetting
        : $$"""{ "tools": [], "vcs": { "settings": [{ "name": "core.longpaths", "expected": "{{expected}}" }] } }""";

    private Task<RuleOutcome> Run(string? manifest, IProcessRunner? processes = null) =>
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
    /// A client that recognises the workspace and answers every setting the
    /// same way.
    /// </summary>
    /// <remarks>
    /// Dispatched on the arguments rather than on the order of calls, because
    /// the rule asks two different questions and a blind substitute would
    /// answer the first with what was meant for the second.
    /// </remarks>
    private static IProcessRunner Repository(ProcessResult setting)
    {
        var processes = Substitute.For<IProcessRunner>();

        processes.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ProcessRequest>().Arguments[0] == "rev-parse"
                ? Printing(".git")
                : setting);

        return processes;
    }

    private static IProcessRunner Recording(List<ProcessRequest> requests, ProcessResult setting)
    {
        var processes = Substitute.For<IProcessRunner>();

        processes.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();

                requests.Add(request);

                return request.Arguments[0] == "rev-parse" ? Printing(".git") : setting;
            });

        return processes;
    }

    /// <summary>A client that answers every question the same way.</summary>
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

    private static ProcessResult Exit(int exitCode) =>
        new(exitCode, string.Empty, string.Empty, TimeSpan.Zero);
}
