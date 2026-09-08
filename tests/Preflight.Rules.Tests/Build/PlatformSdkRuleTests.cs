namespace Preflight.Rules.Tests.Build;

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="PlatformSdkRule"/>.
/// </summary>
/// <remarks>
/// The settings are flat dotted keys — <c>sdk.command</c> rather than an
/// <c>sdk</c> object — because the reader a rule is handed resolves leaf values.
/// An object would read exactly as an absent key, leaving the rule reporting
/// that it checked nothing, forever, with no message to say why.
/// </remarks>
public sealed class PlatformSdkRuleTests
{
    private static readonly string[] VersionArgument = ["--version"];

    private readonly PlatformSdkRule _rule = new();

    private static Dictionary<string, object?> Sdk(
        string? command = "winsdk",
        string? name = "Windows SDK",
        string? minimum = null,
        string? maximum = null)
    {
        var settings = new Dictionary<string, object?> { ["sdk.arguments"] = VersionArgument };

        if (command is not null)
        {
            settings["sdk.command"] = command;
        }

        if (name is not null)
        {
            settings["sdk.name"] = name;
        }

        if (minimum is not null)
        {
            settings["sdk.minimumVersion"] = minimum;
        }

        if (maximum is not null)
        {
            settings["sdk.maximumVersion"] = maximum;
        }

        return settings;
    }

    private static IProcessRunner RunnerPrinting(string output, int exitCode = 0, string standardError = "")
    {
        var runner = Substitute.For<IProcessRunner>();

        runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(exitCode, output, standardError, TimeSpan.FromMilliseconds(30)));

        return runner;
    }

    private static IProcessRunner RunnerThrowing(Exception exception)
    {
        var runner = Substitute.For<IProcessRunner>();

        runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProcessResult>>(_ => throw exception);

        return runner;
    }

    private Task<RuleOutcome> Run(
        IReadOnlyDictionary<string, object?> settings,
        IProcessRunner processes,
        BuildTarget? target = null) =>
        _rule.ExecuteAsync(
            Context(
                policy: PolicyWith(settings),
                processes: processes,
                stage: ValidationStage.BuildReadiness,
                target: target),
            CancellationToken.None);

    [Fact]
    public async Task ExecuteAsync_WithNoSdkConfigured_IsNotApplicable()
    {
        var processes = Substitute.For<IProcessRunner>();

        var outcome = await _rule.ExecuteAsync(
            Context(processes: processes, stage: ValidationStage.BuildReadiness),
            CancellationToken.None);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
        await processes.DidNotReceive().RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    /// <remarks>
    /// The absence of the command is what "not configured" means. Anything else
    /// would start an empty command, which fails in a way that names nothing.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAnSdkThatNamesNoCommand_IsNotApplicable()
    {
        var processes = Substitute.For<IProcessRunner>();

        var outcome = await Run(Sdk(command: null), processes);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
        await processes.DidNotReceive().RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A missing SDK is the workspace being wrong, not the rule breaking.
    /// </summary>
    /// <remarks>
    /// <c>Errored</c> here would say the tool failed. What actually happened is
    /// that the machine does not have the SDK, which is precisely the fact this
    /// rule exists to report.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheCommandIsNotInstalled_Fails()
    {
        var outcome = await Run(Sdk(), RunnerThrowing(new Win32Exception("no such file")));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Status.ShouldNotBe(RuleStatus.Errored);
        outcome.Findings.ShouldHaveSingleItem().Expected.ShouldNotBeNull().ShouldContain("winsdk");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheCommandExitsNonZero_Fails()
    {
        var outcome = await Run(
            Sdk(),
            RunnerPrinting(string.Empty, exitCode: 127, standardError: "not found"));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Actual.ShouldNotBeNull().ShouldContain("not found");
    }

    /// <remarks>
    /// A tool that fails and prints nothing is common — a launcher that exits
    /// silently, or a shell writing its complaint to the other stream. An empty
    /// <c>Actual</c> renders as a label with nothing after it, which reads as the
    /// report being broken rather than the SDK being absent.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheCommandFailsSilently_SaysTheCommandCouldNotBeRun()
    {
        var outcome = await Run(Sdk(), RunnerPrinting(string.Empty, exitCode: 1, standardError: string.Empty));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Actual.ShouldBe("the command could not be run");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no version here")]
    [InlineData("1")]
    [InlineData("nothing numeric at all")]
    public async Task ExecuteAsync_WhenTheOutputCarriesNoVersion_Fails(string output)
    {
        var outcome = await Run(Sdk(), RunnerPrinting(output));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Message.ShouldContain("version");
    }

    /// <remarks>
    /// The ceiling is exclusive, because "anything in 10.x" is written 10.0.0 to
    /// 11.0.0 and an inclusive one would need a version nobody can write down.
    /// </remarks>
    [Theory]
    [InlineData("9.0.400", false)]
    [InlineData("10.0.0", true)]
    [InlineData("10.5.200", true)]
    [InlineData("11.0.0", false)]
    [InlineData("12.0.0", false)]
    public async Task ExecuteAsync_AppliesTheRangeAtItsBoundaries(string version, bool accepted)
    {
        var outcome = await Run(Sdk(minimum: "10.0.0", maximum: "11.0.0"), RunnerPrinting(version));

        outcome.Status.ShouldBe(accepted ? RuleStatus.Passed : RuleStatus.Failed);
    }

    [Theory]
    [InlineData("10.0.0", null, "99.0.0", true, "at least 10.0.0")]
    [InlineData("10.0.0", null, "9.9.9", false, "at least 10.0.0")]
    [InlineData(null, "11.0.0", "9.0.0", true, "below 11.0.0")]
    [InlineData(null, "11.0.0", "11.0.0", false, "below 11.0.0")]
    public async Task ExecuteAsync_WithAnOpenEndedRange_OnlyAppliesTheBoundGiven(
        string? minimum,
        string? maximum,
        string version,
        bool accepted,
        string expected)
    {
        var outcome = await Run(Sdk(minimum: minimum, maximum: maximum), RunnerPrinting(version));

        outcome.Status.ShouldBe(accepted ? RuleStatus.Passed : RuleStatus.Failed);

        if (!accepted)
        {
            outcome.Findings.ShouldHaveSingleItem().Expected.ShouldBe(expected);
        }
    }

    /// <remarks>
    /// Ignored rather than obeyed, in both directions: read as zero the rule
    /// would accept everything and be silently toothless, and read as infinity
    /// it could never be satisfied.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAnUnparseableBound_IgnoresItAndKeepsTheOther()
    {
        var settings = Sdk(minimum: "ten", maximum: "11.0.0");

        (await Run(settings, RunnerPrinting("1.0.0"))).Status.ShouldBe(RuleStatus.Passed);
        (await Run(settings, RunnerPrinting("11.0.0"))).Status.ShouldBe(RuleStatus.Failed);
    }

    /// <summary>
    /// The finding names the platform it is talking about.
    /// </summary>
    /// <remarks>
    /// This rule exists because the toolchain rule is blind to the target, so a
    /// report that did not say which target the SDK was wanted for would be
    /// indistinguishable from the rule it replaced. With no target given the
    /// effective one is the literal <c>any</c>, which is a real target name and
    /// reads honestly.
    /// </remarks>
    [Theory]
    [InlineData("win64")]
    [InlineData("ps5")]
    [InlineData("any")]
    public async Task ExecuteAsync_NamesTheTargetPlatformInTheFinding(string platform)
    {
        var outcome = await Run(
            Sdk(),
            RunnerThrowing(new Win32Exception("no such file")),
            new BuildTarget(platform, "Development"));

        var finding = outcome.Findings.ShouldHaveSingleItem();

        $"{finding.Message} {finding.Expected} {finding.Actual} {finding.Remediation}".ShouldContain(platform);
    }

    /// <summary>
    /// The rule reads its own settings and nothing else.
    /// </summary>
    /// <remarks>
    /// The target layer has already specialised those settings before the rule
    /// runs. A rule that reached for the target key itself would be reading
    /// another rule's configuration — and could not, because the reader it is
    /// handed does not carry the root.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_ReadsNoKeyOutsideItsOwnSdkSettings()
    {
        var recording = new RecordingPolicy(Sdk());

        await _rule.ExecuteAsync(
            Context(policy: recording, processes: RunnerPrinting("10.0.100"), stage: ValidationStage.BuildReadiness),
            CancellationToken.None);

        recording.Keys.ShouldNotBeEmpty();
        recording.Keys.ShouldAllBe(key => !key.StartsWith("targets", StringComparison.Ordinal));
    }

    /// <remarks>
    /// A deadline that expired is <c>Errored</c> and the tool's verdict to give.
    /// A rule that caught its own cancellation would report the workspace as
    /// broken when what happened is that a command ran long.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheProcessIsCancelled_DoesNotSwallowIt()
    {
        await Should.ThrowAsync<OperationCanceledException>(() =>
            Run(Sdk(), RunnerThrowing(new OperationCanceledException())));
    }

    [Fact]
    public async Task ExecuteAsync_RunsTheCommandInTheWorkspaceRoot()
    {
        var processes = RunnerPrinting("10.0.100");

        await Run(Sdk(), processes);

        await processes.Received(1).RunAsync(
            Arg.Is<ProcessRequest>(request =>
                request.FileName == "winsdk"
                && request.Arguments.Contains("--version")
                && request.WorkingDirectory == WorkspaceRoot.FullName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithACancelledToken_StopsBeforeRunningAnything()
    {
        var processes = RunnerPrinting("10.0.100");

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _rule.ExecuteAsync(
                Context(
                    policy: PolicyWith(Sdk()),
                    processes: processes,
                    stage: ValidationStage.BuildReadiness),
                cancellation.Token));

        await processes.DidNotReceive().RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A policy reader that writes down every key it is asked for.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than substituted, for the reason the fixture's own
    /// stub is: the interface is generic in a way that would need each type
    /// argument configured separately, and a silent default on the one nobody
    /// named is exactly the mistake this test is watching for.
    /// </remarks>
    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Constructed by the test below.")]
    private sealed class RecordingPolicy : IPolicyReader
    {
        private readonly IReadOnlyDictionary<string, object?> _values;

        private readonly List<string> _keys = [];

        public RecordingPolicy(IReadOnlyDictionary<string, object?> values)
        {
            _values = values;
        }

        public IReadOnlyList<string> Keys => _keys;

        public T GetValue<T>(string key, T fallback) =>
            TryGetValue<T>(key, out var value) ? value : fallback;

        public bool TryGetValue<T>(
            string key,
            [MaybeNullWhen(false)] out T value)
        {
            _keys.Add(key);

            if (_values.TryGetValue(key, out var stored) && stored is T typed)
            {
                value = typed;

                return true;
            }

            value = default;

            return false;
        }
    }
}
