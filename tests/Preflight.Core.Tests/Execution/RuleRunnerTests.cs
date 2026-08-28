namespace Preflight.Core.Tests.Execution;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Core;
using static Preflight.Core.Tests.Graph.GraphFixture;

/// <summary>
/// Fixes rule isolation: one rule, no graph, no
/// level, no parallelism.
/// </summary>
/// <remarks>
/// <para>
/// A rule that throws becomes <c>Errored</c> with its stack trace, and the run
/// continues. A rule that overruns its timeout becomes <c>Errored</c> too, never
/// <c>Failed</c> — a rule that did not finish never said the workspace was
/// wrong, it said that it itself was.
/// </para>
/// <para>
/// Nothing here waits on a clock. Every timeout case uses a rule that never
/// completes on its own, so the timeout is the only event that can happen and
/// the race disappears instead of being tolerated. A tolerance-based timeout
/// test passes on a fast machine, fails on a loaded CI agent, and gets deleted
/// as flaky by someone who never learns what it guarded — the test strategy makes
/// exactly that warning about the determinism test.
/// </para>
/// </remarks>
public sealed class RuleRunnerTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task RunAsync_WithAPassingRule_RecordsThePolicySnapshotAndANonNegativeDuration()
    {
        var rule = FakeRule.Passing("core.a.alpha");
        var snapshot = Snapshot(rule, blocking: false, gating: false, severity: Severity.Warning);

        var execution = await Run(rule, snapshot);

        execution.Status.ShouldBe(RuleStatus.Passed);
        execution.Blocking.ShouldBeFalse();
        execution.Gating.ShouldBeFalse();
        execution.EffectiveSeverity.ShouldBe(Severity.Warning);
        execution.FromCache.ShouldBeFalse();

        // Monotonic only. Asserting a magnitude would be asserting the speed of
        // the machine, and a rule returning a completed task can measure zero.
        execution.Duration.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task RunAsync_WithEachRuleStatusARuleCanReturn_RecordsIt()
    {
        (await Run(FakeRule.Passing("core.a.alpha"))).Status.ShouldBe(RuleStatus.Passed);
        (await Run(FakeRule.Warning("core.a.bravo"))).Status.ShouldBe(RuleStatus.Warning);
        (await Run(FakeRule.Failing("core.a.charlie"))).Status.ShouldBe(RuleStatus.Failed);
        (await Run(FakeRule.NotApplicable("core.a.delta"))).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// A finding carries no severity of its own, which leaves ordering as the only
    /// channel a rule has to say one finding matters more than another. The
    /// runner must not reorder them.
    /// </remarks>
    [Fact]
    public async Task RunAsync_PreservesTheFindingOrderTheRuleProduced()
    {
        var findings = new[]
        {
            new Finding { Message = "third" },
            new Finding { Message = "first" },
            new Finding { Message = "second" },
        };

        var execution = await Run(FakeRule.WithFindings("core.a.alpha", findings));

        execution.Findings.Select(finding => finding.Message).ShouldBe(["third", "first", "second"]);
    }

    [Fact]
    public async Task RunAsync_WhenTheRuleThrows_RecordsErroredWithTheStackTraceAndDoesNotPropagate()
    {
        var execution = await Run(FakeRule.Throwing("core.a.alpha", "the rule blew up"));

        execution.Status.ShouldBe(RuleStatus.Errored);
        execution.ErrorDetail.ShouldNotBeNull();
        execution.ErrorDetail!.ShouldContain("the rule blew up");
        execution.ErrorDetail.ShouldContain("at ");
    }

    /// <remarks>
    /// The sibling of the test above, and not a duplicate of it: a rule that is
    /// not <c>async</c> throws before the runner ever holds a task, while a
    /// faulted task throws out of the await. They are two different places for
    /// the isolation to have a hole in.
    /// </remarks>
    [Fact]
    public async Task RunAsync_WhenTheRuleReturnsAFaultedTask_RecordsErroredWithTheRealException()
    {
        var execution = await Run(FakeRule.ThrowingAsync("core.a.alpha", "the task faulted"));

        execution.Status.ShouldBe(RuleStatus.Errored);
        execution.ErrorDetail.ShouldNotBeNull();
        execution.ErrorDetail!.ShouldContain("the task faulted");

        // The real exception, not an AggregateException wrapper: "One or more
        // errors occurred" in a report costs a debugging session.
        execution.ErrorDetail.ShouldNotContain(nameof(AggregateException));
    }

    /// <remarks>
    /// The token assertion is the half that matters operationally: rule isolation
    /// requires a timed-out rule's child process to be killed through the token
    /// the rule holds. Without it, a timeout leaves an orphan compiler running
    /// on a build machine.
    /// </remarks>
    [Fact]
    public async Task RunAsync_WhenTheRuleExceedsItsTimeout_RecordsErroredAndCancelsTheTokenTheRuleHolds()
    {
        var rule = FakeRule.Hanging("core.a.alpha");
        var snapshot = Snapshot(rule, timeout: TimeSpan.FromMilliseconds(20));

        var execution = await Run(rule, snapshot);

        execution.Status.ShouldBe(RuleStatus.Errored, "A timeout is a defect of the rule, never of the workspace.");
        execution.ErrorDetail.ShouldNotBeNull();
        execution.ErrorDetail!.ShouldContain("timed out");
        rule.SeenToken.IsCancellationRequested.ShouldBeTrue();
    }

    /// <remarks>
    /// The run token is cancelled only after the rule signals that it has
    /// entered, so the cancellation lands at a known point instead of wherever
    /// the scheduler happened to be. The error must blame the run, not the rule:
    /// a stack trace here would accuse a rule of a user pressing Ctrl+C.
    /// </remarks>
    [Fact]
    public async Task RunAsync_WhenTheRunIsCancelled_RecordsErroredNamingTheRunNotTheRule()
    {
        using var runCancellation = new CancellationTokenSource();
        var rule = FakeRule.Hanging("core.a.alpha");

        var running = new RuleRunner(TimeProvider.System)
            .RunAsync(rule, Snapshot(rule), Context(rule), runCancellation.Token);

        await rule.Started.Task.WaitAsync(Generous, TestContext.Current.CancellationToken);
        await runCancellation.CancelAsync();

        var execution = await running;

        execution.Status.ShouldBe(RuleStatus.Errored);
        execution.ErrorDetail.ShouldNotBeNull();
        execution.ErrorDetail!.ShouldContain("run was cancelled");
        execution.ErrorDetail.ShouldNotContain("at ");
    }

    [Fact]
    public async Task RunAsync_WithAnAlreadyCancelledRunToken_RecordsErroredWithoutInvokingTheRule()
    {
        using var runCancellation = new CancellationTokenSource();
        await runCancellation.CancelAsync();

        var rule = FakeRule.Passing("core.a.alpha");

        var execution = await new RuleRunner(TimeProvider.System)
            .RunAsync(rule, Snapshot(rule), Context(rule), runCancellation.Token);

        execution.Status.ShouldBe(RuleStatus.Errored);
        rule.Invoked.ShouldBeFalse("Rules that have not started must not start.");
    }

    /// <remarks>
    /// A rule that never honours its token cannot be stopped —.NET has no way
    /// to abort a task. The runner abandons it and returns, because waiting
    /// forever would make the timeout advisory and hang CI on a bad plugin. The
    /// abandoned task's fault still has to be observed, or it resurfaces as an
    /// unobserved exception that tears down the process during some later,
    /// unrelated test.
    /// </remarks>
    [Fact]
    public async Task RunAsync_WhenTheRuleIgnoresCancellation_ReturnsAnywayAndObservesTheAbandonedFault()
    {
        var release = new TaskCompletionSource<RuleOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rule = FakeRule.Ignoring("core.a.alpha", release);
        var loggers = new RecordingRuleLoggerFactory();

        var execution = await new RuleRunner(TimeProvider.System).RunAsync(
            rule,
            Snapshot(rule, timeout: TimeSpan.FromMilliseconds(20)),
            Context(rule, loggers),
            TestContext.Current.CancellationToken);

        execution.Status.ShouldBe(RuleStatus.Errored);

        release.SetException(new InvalidOperationException("late failure from an abandoned rule"));

        await WaitUntil(() => loggers.MessagesFor(rule.Descriptor.Id).Count > 0);

        loggers.MessagesFor(rule.Descriptor.Id).ShouldContain(message => message.Contains("abandoned"));
    }

    /// <remarks>
    /// The outcome contract reserves <c>Skipped</c> and <c>Errored</c> for the engine, and
    /// offers a rule no factory for either — but <c>RuleOutcome.Status</c> is a
    /// public init property, so a rule can still claim one. Passing it through
    /// would put a <c>skipped</c> in the report with no cause attached, which is
    /// precisely what skip attribution exists to prevent.
    /// </remarks>
    [Theory]
    [InlineData(RuleStatus.Skipped)]
    [InlineData(RuleStatus.Errored)]
    public async Task RunAsync_WithARuleThatSelfDeclaresAnEngineOnlyStatus_RecordsErroredNamingTheContractViolation(
        RuleStatus claimed)
    {
        var execution = await Run(FakeRule.SelfDeclaring("core.a.alpha", claimed));

        execution.Status.ShouldBe(RuleStatus.Errored);
        execution.ErrorDetail.ShouldNotBeNull();
        execution.ErrorDetail!.ShouldContain(claimed.ToString());
        execution.SkippedBecauseOf.ShouldBeEmpty();
    }

    /// <remarks>
    /// Nullable reference types are a compile-time promise, not a runtime one: a
    /// plugin built without them, or against an older contract, can still hand
    /// back nothing. Treating that as a default-constructed pass would put a
    /// green line in the report for a rule that answered nothing at all.
    /// </remarks>
    [Fact]
    public async Task RunAsync_WhenTheRuleReturnsNoOutcome_RecordsErrored()
    {
        var execution = await Run(FakeRule.ReturningNull("core.a.alpha"));

        execution.Status.ShouldBe(RuleStatus.Errored);
        execution.ErrorDetail.ShouldNotBeNull();
        execution.ErrorDetail!.ShouldContain("no outcome");
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Generous;

        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private static Task<RuleExecution> Run(FakeRule rule, RulePolicySnapshot? snapshot = null) =>
        new RuleRunner(TimeProvider.System).RunAsync(
            rule, snapshot ?? Snapshot(rule), Context(rule), TestContext.Current.CancellationToken);

    private static RulePolicySnapshot Snapshot(
        FakeRule rule,
        bool blocking = true,
        bool gating = true,
        Severity severity = Severity.Error,
        TimeSpan? timeout = null) => new()
        {
            RuleId = rule.Descriptor.Id,
            Enabled = true,
            Blocking = blocking,
            Gating = gating,
            EffectiveSeverity = severity,
            Timeout = timeout ?? TimeSpan.FromSeconds(60),
        };

    private static RuleContext Context(FakeRule rule, RecordingRuleLoggerFactory? loggers = null)
    {
        var descriptors = new[] { rule.Descriptor };
        var policy = PolicyFixture.For().Build(descriptors);

        return new RuleContext
        {
            WorkspaceRoot = new DirectoryInfo(Path.GetTempPath()),
            Stage = ValidationStage.PreSubmit,
            Target = new BuildTarget("x64", "Debug"),
            ChangedFiles = [],
            Policy = policy.ReaderFor(rule.Descriptor.Id),
            Logger = (loggers ?? new RecordingRuleLoggerFactory()).ForRule(rule.Descriptor.Id),
            FileSystem = Substitute.For<IFileSystem>(),
            Processes = Substitute.For<IProcessRunner>(),
        };
    }
}
