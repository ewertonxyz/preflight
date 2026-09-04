namespace Preflight.Core.Tests.Execution;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core;
using Preflight.Core.Execution;
using Preflight.Core.Policy;

/// <summary>
/// Fixes the engine end to end: levels, parallelism, skip propagation between
/// levels, verdict aggregation, and the deterministic result of
/// the determinism guarantee.
/// </summary>
/// <remarks>
/// <para>
/// Not one assertion here measures elapsed time. Overlap is proved with a gate
/// that only opens once the expected number of rules has entered; ordering is
/// proved by releasing gates in the reverse of the expected presentation order;
/// cancellation is triggered after a rule signals that it started. A test that
/// used delays instead would be asserting the speed of the machine, would pass
/// locally, and would be deleted the first time CI was busy.
/// </para>
/// <para>
/// Every wait is bounded and carries a message, so a regression that serialises
/// a level fails as a named assertion rather than hanging the suite with no
/// diagnosis.
/// </para>
/// </remarks>
public sealed class RuleExecutorTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ExecuteAsync_BuildsEachRuleAContextScopedToItself()
    {
        var alpha = FakeRule.Passing("core.a.alpha");
        var bravo = FakeRule.Passing("core.a.bravo");
        var request = Request([alpha, bravo]);

        await Execute(request);

        alpha.SeenContext.ShouldNotBeNull();
        alpha.SeenContext!.Stage.ShouldBe(request.Stage);
        alpha.SeenContext.Target.ShouldBe(request.Target);
        alpha.SeenContext.FileSystem.ShouldBeSameAs(request.FileSystem);
        alpha.SeenContext.Processes.ShouldBeSameAs(request.Processes);

        // Each rule gets its own reader and its own logger, per the context services.
        alpha.SeenContext.Policy.ShouldNotBeSameAs(bravo.SeenContext!.Policy);
        alpha.SeenContext.Logger.ShouldNotBeSameAs(bravo.SeenContext.Logger);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoRules_IsPassedWithNoExecutions()
    {
        var result = await Execute(Request([]));

        result.Executions.ShouldBeEmpty();
        result.Verdict.ShouldBe(RunVerdict.Passed);
        result.Partial.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithADisabledRule_OmitsItFromExecutions()
    {
        var alpha = FakeRule.Passing("core.a.alpha");
        var bravo = FakeRule.Passing("core.a.bravo");

        var result = await Execute(Request(
            [alpha, bravo],
            PolicyFixture.For().Rule("core.a.bravo", enabled: false)));

        result.Executions.Select(execution => execution.RuleId.Value).ShouldBe(["core.a.alpha"]);
        bravo.Invoked.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_RecordsTheRequestedRunIdStageTargetPipelineAndPolicyChainVerbatim()
    {
        var request = Request([FakeRule.Passing("core.a.alpha")]);

        var result = await Execute(request);

        result.RunId.ShouldBe(RunFixture.FixedRunId);
        result.Stage.ShouldBe(request.Stage);
        result.Target.ShouldBe(request.Target);
        result.Pipeline.ShouldBe("atlas");
        result.PolicyChain.ShouldBe(["preflight.base.json", "preflight.atlas.json"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutARequestedRunId_GeneratesADistinctOnePerRun()
    {
        var first = await Execute(Request([FakeRule.Passing("core.a.alpha")]) with { RunId = null });
        var second = await Execute(Request([FakeRule.Passing("core.a.alpha")]) with { RunId = null });

        first.RunId.ShouldNotBe(second.RunId);
        first.RunId.ShouldNotBe(Guid.Empty);
    }

    /// <remarks>
    /// The sibling is not gated: if it did not run, that is isolation being
    /// broken, not a timing artefact.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithARuleThatThrows_FinishesTheLevelAndErrorsTheRun()
    {
        var thrower = FakeRule.Throwing("core.a.alpha", "boom");
        var sibling = FakeRule.Passing("core.a.bravo");

        var result = await Execute(Request([thrower, sibling]));

        Status(result, "core.a.alpha").ShouldBe(RuleStatus.Errored);
        Status(result, "core.a.bravo").ShouldBe(RuleStatus.Passed);
        sibling.Invoked.ShouldBeTrue();
        result.Verdict.ShouldBe(RunVerdict.Errored);
    }

    /// <remarks>
    /// Keeps exit 2 distinct from exit 3 (the exit-code contract). A misbehaving rule is a
    /// verdict, never an exception escaping the engine — if it escaped, the CLI
    /// would have no result to map and would report a configuration error for
    /// what is a rule defect.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithARuleThatMisbehaves_NeverThrowsOutOfTheRun()
    {
        var result = await Execute(Request([
            FakeRule.Throwing("core.a.alpha", "boom"),
            FakeRule.SelfDeclaring("core.a.bravo", RuleStatus.Skipped),
        ]));

        result.Executions.Count.ShouldBe(2);
    }

    /// <remarks>
    /// The four cells of the two axes, each asserting the verdict <em>and</em>
    /// whether the dependent ran, independently. The separation exists because one
    /// field used to decide both, making two of these four inexpressible.
    /// </remarks>
    [Theory]
    [InlineData(true, true, RunVerdict.Blocked, false)]
    [InlineData(true, false, RunVerdict.Blocked, true)]
    [InlineData(false, true, RunVerdict.PassedWithWarnings, false)]
    [InlineData(false, false, RunVerdict.PassedWithWarnings, true)]
    public async Task ExecuteAsync_WithTheFourBlockingGatingCombinations_ProducesTheVerdictAndTheDependentOutcome(
        bool blocking, bool gating, RunVerdict expectedVerdict, bool dependentShouldRun)
    {
        var root = FakeRule.Failing("core.a.charlie");
        var dependent = FakeRule.Passing("core.a.alpha", "core.a.charlie");

        var result = await Execute(Request(
            [root, dependent],
            PolicyFixture.For().Rule("core.a.charlie", blocking: blocking, gating: gating)));

        result.Verdict.ShouldBe(expectedVerdict);
        dependent.Invoked.ShouldBe(dependentShouldRun);

        Status(result, "core.a.alpha").ShouldBe(dependentShouldRun ? RuleStatus.Passed : RuleStatus.Skipped);
    }

    /// <remarks>
    /// Gating is read from effective policy, not from the descriptor. Same rule,
    /// same code, opposite behaviour under a different overlay — which is the
    /// whole premise of policy.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_ReadsGatingFromPolicyNotFromTheDescriptorDefault()
    {
        var root = FakeRule.Failing("core.a.charlie");
        var dependent = FakeRule.Passing("core.a.alpha", "core.a.charlie");

        root.Descriptor.DefaultGating.ShouldBeTrue("The descriptor says gate; the policy will say otherwise.");

        var result = await Execute(Request(
            [root, dependent],
            PolicyFixture.For().Rule("core.a.charlie", gating: false)));

        dependent.Invoked.ShouldBeTrue();
        result.Verdict.ShouldBe(RunVerdict.Blocked);
    }

    /// <remarks>
    /// The guard against the two orderings drifting apart: <c>ExecutionSet</c>
    /// lists disabled dependencies ordinally, while skip attribution wants the
    /// report ordered by depth. The ids here are chosen so the two disagree, and
    /// the assertion is on the <c>RunResult</c>, which is where 7.3 applies.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithDisabledDependenciesInDifferentLevels_OrdersSkippedBecauseOfByDepth()
    {
        var zulu = FakeRule.Passing("core.a.zulu");
        var mid = FakeRule.Passing("core.a.mid", "core.a.zulu");
        var leaf = FakeRule.Passing("core.a.leaf", "core.a.mid");

        var result = await Execute(Request(
            [zulu, mid, leaf],
            PolicyFixture.For()
                .Rule("core.a.zulu", enabled: false)
                .Rule("core.a.mid", enabled: false)));

        var skipped = result.Executions.Single(execution => execution.RuleId.Value == "core.a.leaf");

        skipped.Status.ShouldBe(RuleStatus.Skipped);
        skipped.SkipReason.ShouldBe(SkipReason.DependencyDisabled);
        skipped.SkippedBecauseOf.Select(id => id.Value).ShouldBe(["core.a.zulu", "core.a.mid"]);
    }

    [Fact]
    public async Task ExecuteAsync_RunsALevelConcurrentlyAndNeverExceedsMaxDegreeOfParallelism()
    {
        const int Limit = 3;
        var entered = 0;
        var peak = 0;
        var allEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var rules = Enumerable.Range(0, 6).Select(index => FakeRule.Custom(
            $"core.a.rule-{index}",
            async (_, token) =>
            {
                var now = Interlocked.Increment(ref entered);
                InterlockedMax(ref peak, now);

                if (now == Limit)
                {
                    allEntered.TrySetResult();
                }

                // Holding the first batch open is what proves overlap: if the
                // level were serial, the gate would never open and the bounded
                // wait below would fail with a message instead of hanging.
                await allEntered.Task.WaitAsync(Generous, token);
                Interlocked.Decrement(ref entered);

                return RuleOutcome.Passed();
            })).ToArray();

        var result = await Execute(Request(rules, PolicyFixture.For().Root(maxDegreeOfParallelism: Limit)));

        result.Verdict.ShouldBe(RunVerdict.Passed);
        peak.ShouldBe(Limit, "A level must run concurrently, and never above the configured limit.");
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotStartALevelUntilThePreviousOneHasFinished()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = FakeRule.Gated("core.a.charlie", gate, RuleOutcome.Passed());
        var second = FakeRule.Passing("core.a.alpha", "core.a.charlie");

        var running = Execute(Request([first, second]));

        await first.Started.Task.WaitAsync(Generous, TestContext.Current.CancellationToken);
        second.Invoked.ShouldBeFalse("A level is a barrier: the next one cannot start early.");

        gate.SetResult();
        await running;

        second.Invoked.ShouldBeTrue();
    }

    /// <remarks>
    /// The completion order is forced to be the reverse of the presentation
    /// order, so a result emitted in completion order fails every time rather
    /// than occasionally. This is the second of the two tests the test strategy marks.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithCompletionOrderInverted_EmitsExecutionsInTheDeterministicOrder()
    {
        var firstGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var alpha = FakeRule.Gated("core.a.alpha", firstGate, RuleOutcome.Passed());
        var bravo = FakeRule.Gated("core.a.bravo", secondGate, RuleOutcome.Passed());

        var running = Execute(Request([alpha, bravo], PolicyFixture.For().Root(maxDegreeOfParallelism: 2)));

        await alpha.Started.Task.WaitAsync(Generous, TestContext.Current.CancellationToken);
        await bravo.Started.Task.WaitAsync(Generous, TestContext.Current.CancellationToken);

        // bravo finishes first; alpha must still be presented first.
        secondGate.SetResult();
        firstGate.SetResult();

        var result = await running;

        result.Executions.Select(execution => execution.RuleId.Value).ShouldBe(["core.a.alpha", "core.a.bravo"]);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheRunIsCancelled_IsPartialAndOmitsTheRulesThatNeverStarted()
    {
        using var cancellation = new CancellationTokenSource();
        var first = FakeRule.Hanging("core.a.charlie");
        var second = FakeRule.Passing("core.a.alpha", "core.a.charlie");

        var running = ExecuteWith(Request([first, second]), cancellation.Token);

        await first.Started.Task.WaitAsync(Generous, TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        var result = await running;

        result.Verdict.ShouldBe(RunVerdict.Errored);
        result.Partial.ShouldBeTrue();
        second.Invoked.ShouldBeFalse();
        result.Executions.Select(execution => execution.RuleId.Value).ShouldNotContain("core.a.alpha");
        Status(result, "core.a.charlie").ShouldBe(RuleStatus.Errored);
    }

    /// <remarks>
    /// The inconsistency this test exists for: with nothing in flight, no
    /// execution is <c>Errored</c>, so a verdict computed purely from the
    /// executions would come out <c>Passed</c>. The concurrency contract allows no such
    /// exception — a cancelled run is <c>Errored</c>.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenCancelledBetweenLevels_IsErroredEvenThoughNoExecutionErrored()
    {
        using var cancellation = new CancellationTokenSource();

        // Completes synchronously: an awaiting rule would be suspended at the
        // moment it cancels, and the runner would legitimately see the token win
        // the race. Returning an already-completed task removes the race rather
        // than tolerating it — Task.WaitAsync short-circuits on a completed task
        // without consulting the token.
        var first = FakeRule.Custom("core.a.charlie", (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(RuleOutcome.Passed());
        });

        var second = FakeRule.Passing("core.a.alpha", "core.a.charlie");

        var result = await ExecuteWith(Request([first, second]), cancellation.Token);

        Status(result, "core.a.charlie").ShouldBe(RuleStatus.Passed);
        result.Executions.ShouldAllBe(execution => execution.Status != RuleStatus.Errored);
        result.Verdict.ShouldBe(RunVerdict.Errored);
        result.Partial.ShouldBeTrue();
        second.Invoked.ShouldBeFalse();
    }

    /// <remarks>
    /// Cancellation arriving once every rule has been recorded leaves a complete
    /// record, so the run is not partial — but it is still <c>Errored</c>, which
    /// the concurrency contract states without exception. The two answer different questions:
    /// the verdict says the run was interrupted, <c>Partial</c> says whether
    /// anything is missing from the history. Conflating them would either bias
    /// the duration percentiles of the instrumentation or hide an interruption entirely.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenCancellationArrivesAfterTheLastRule_IsErroredButNotPartial()
    {
        using var cancellation = new CancellationTokenSource();

        var only = FakeRule.Custom("core.a.alpha", (_, _) =>
        {
            var outcome = Task.FromResult(RuleOutcome.Passed());
            cancellation.Cancel();
            return outcome;
        });

        var result = await ExecuteWith(Request([only]), cancellation.Token);

        result.Partial.ShouldBeFalse();
        result.Verdict.ShouldBe(RunVerdict.Errored);
    }

    /// <remarks>
    /// Both halves matter. Without the recorded flag, the history's <c>report</c>
    /// reads a run blocked by a warning under a CI flag as one blocked by a
    /// blocking failure — and a metric that conflates the two overstates what
    /// the tool caught.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithFailOnWarning_RecordsTheFlagAndBlocksTheRun()
    {
        var result = await Execute(Request([FakeRule.Warning("core.a.alpha")]) with { FailOnWarning = true });

        result.Verdict.ShouldBe(RunVerdict.Blocked);
        result.FailOnWarning.ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutFailOnWarning_RecordsTheFlagAsFalse()
    {
        var result = await Execute(Request([FakeRule.Warning("core.a.alpha")]));

        result.Verdict.ShouldBe(RunVerdict.PassedWithWarnings);
        result.FailOnWarning.ShouldBeFalse();
    }

    /// <remarks>
    /// Asserts only that the dependents run. The verdict is deliberately not
    /// asserted: the contrast flag claims the flag does not change it, while dependents
    /// that now execute can fail on their own — a contradiction in the design
    /// that is still open.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithNoSkip_RunsTheDependentsOfAGatingFailure()
    {
        var root = FakeRule.Failing("core.a.charlie");
        var dependent = FakeRule.Passing("core.a.alpha", "core.a.charlie");

        await Execute(Request([root, dependent]) with { NoSkip = true });

        dependent.Invoked.ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_RecordsTheEffectivePolicyOnEveryExecutionIncludingSkippedOnes()
    {
        var root = FakeRule.Failing("core.a.charlie");
        var dependent = FakeRule.Passing("core.a.alpha", "core.a.charlie");

        var result = await Execute(Request(
            [root, dependent],
            PolicyFixture.For()
                .Rule("core.a.charlie", severity: "warning")
                .Rule("core.a.alpha", blocking: false, severity: "information")));

        var skipped = result.Executions.Single(execution => execution.RuleId.Value == "core.a.alpha");

        skipped.Status.ShouldBe(RuleStatus.Skipped);
        skipped.EffectiveSeverity.ShouldBe(Severity.Information);
        skipped.Blocking.ShouldBeFalse();
        skipped.Duration.ShouldBe(TimeSpan.Zero);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;

        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }

    private static RuleStatus Status(RunResult result, string ruleId) =>
        result.Executions.Single(execution => execution.RuleId.Value == ruleId).Status;

    private static RunRequest Request(IReadOnlyList<FakeRule> rules, PolicyFixture? policy = null)
    {
        var descriptors = RunFixture.DescriptorsOf(rules);

        return RunFixture.For(rules, (policy ?? PolicyFixture.For()).Build(descriptors));
    }

    /// <remarks>
    /// Deliberately takes no token: xUnit1051 asks every call that accepts one
    /// to pass the test's own, and threading it through every arrange line would
    /// bury the thing each test is actually about. The cancellation tests use
    /// <see cref="ExecuteWith"/> instead, where the token is the point.
    /// </remarks>
    private static Task<RunResult> Execute(RunRequest request) =>
        ExecuteWith(request, TestContext.Current.CancellationToken);

    private static Task<RunResult> ExecuteWith(RunRequest request, CancellationToken cancellationToken) =>
        new RuleExecutor(new RecordingRuleLoggerFactory(), TimeProvider.System)
            .ExecuteAsync(request, cancellationToken);
}
