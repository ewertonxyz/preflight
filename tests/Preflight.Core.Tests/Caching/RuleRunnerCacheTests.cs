namespace Preflight.Core.Tests.Caching;

using Preflight.Abstractions;
using Preflight.Core;
using Preflight.Core.Caching;
using Preflight.Core.Tests.Execution;
using Preflight.TestSupport;
using static Preflight.Core.Tests.Caching.CacheFixture;

/// <summary>
/// The runner with a cache attached: what it skips, what it stores, and what it
/// refuses to let the cache cost.
/// </summary>
/// <remarks>
/// Nothing here waits on a clock. The two deadline cases use a fingerprint that
/// never completes on its own, so the timeout or the cancellation is the only
/// event that can happen — the same technique <c>RuleRunnerTests</c> uses, and
/// for the same reason: a tolerance-based test passes on a fast machine and gets
/// deleted as flaky by somebody who never learns what it guarded.
/// </remarks>
public sealed class RuleRunnerCacheTests
{
    private readonly RecordingRuleLoggerFactory _loggers = new();

    /// <summary>
    /// A hit returns the stored result, says so, and does not run the rule.
    /// </summary>
    /// <remarks>
    /// All three halves matter. Without the flag the report claims a check ran
    /// when it did not, which the cache key makes the condition on which the
    /// whole cache is acceptable; without the rule staying unexecuted the cache
    /// saves nothing.
    /// </remarks>
    [Fact]
    public async Task RunAsync_ForAStoredResult_ReturnsItMarkedAsCachedWithoutExecuting()
    {
        var rule = FakeCacheableRule.Describing("core.a.alpha");
        var store = new RecordingCacheStore();

        await Seed(store, rule, RuleOutcome.Failed(new Finding { Message = "from the cache" }));

        var execution = await Run(rule, store);

        execution.FromCache.ShouldBeTrue();
        execution.Status.ShouldBe(RuleStatus.Failed);
        execution.Findings.ShouldHaveSingleItem().Message.ShouldBe("from the cache");
        rule.Executions.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_ForAMiss_ExecutesAndStoresTheResult()
    {
        var rule = FakeCacheableRule.Describing("core.a.alpha");
        var store = new RecordingCacheStore();

        var execution = await Run(rule, store);

        execution.FromCache.ShouldBeFalse();
        rule.Executions.ShouldBe(1);
        store.Entries.ShouldHaveSingleItem();
    }

    /// <remarks>
    /// A second run over an unchanged workspace is the loop the cache exists
    /// for, so it is asserted as one sequence rather than inferred from two
    /// tests.
    /// </remarks>
    [Fact]
    public async Task RunAsync_TwiceOverUnchangedInputs_ExecutesOnce()
    {
        var rule = FakeCacheableRule.Describing("core.a.alpha");
        var store = new RecordingCacheStore();

        (await Run(rule, store)).FromCache.ShouldBeFalse();
        (await Run(rule, store)).FromCache.ShouldBeTrue();

        rule.Executions.ShouldBe(1);
    }

    /// <remarks>
    /// A changed fingerprint is a different key, so the second run misses. This
    /// is the invalidation the whole design turns on, and getting it wrong is
    /// the one failure that reports a <c>Passed</c> over a workspace that
    /// changed.
    /// </remarks>
    [Fact]
    public async Task RunAsync_AfterTheFingerprintChanges_ExecutesAgain()
    {
        var store = new RecordingCacheStore();

        await Run(FakeCacheableRule.Describing("core.a.alpha", "before"), store);

        var changed = FakeCacheableRule.Describing("core.a.alpha", "after");

        (await Run(changed, store)).FromCache.ShouldBeFalse();

        changed.Executions.ShouldBe(1);
        store.Entries.Count.ShouldBe(2);
    }

    /// <remarks>
    /// No cache, no fingerprint. The cost of describing inputs is only worth
    /// paying when somebody is going to compare the description.
    /// </remarks>
    [Fact]
    public async Task RunAsync_WithNoCache_NeverAsksForAFingerprint()
    {
        var rule = FakeCacheableRule.Describing("core.a.alpha");

        var execution = await new RuleRunner(TimeProvider.System).RunAsync(
            rule, SnapshotFor(rule), ContextFor(rule, _loggers), TestContext.Current.CancellationToken);

        execution.FromCache.ShouldBeFalse();
        rule.Fingerprints.ShouldBe(0);
        rule.Executions.ShouldBe(1);
    }

    /// <remarks>
    /// The cache contract: a rule that exploded has to explode again.
    /// </remarks>
    [Fact]
    public async Task RunAsync_ForARuleThatThrows_StoresNothing()
    {
        var rule = FakeCacheableRule.Describing("core.a.alpha", outcome: new RuleOutcome
        {
            Status = RuleStatus.Errored,
        });

        var store = new RecordingCacheStore();

        (await Run(rule, store)).Status.ShouldBe(RuleStatus.Errored);

        store.Writes.ShouldBe(0);
    }

    /// <remarks>
    /// The rule still runs. A fingerprint that breaks says the cache cannot be
    /// used, not that the workspace is wrong.
    /// </remarks>
    [Fact]
    public async Task RunAsync_ForARuleWhoseFingerprintThrows_StillExecutesAndStoresNothing()
    {
        var rule = FakeCacheableRule.Breaking("core.a.alpha", "no such directory");
        var store = new RecordingCacheStore();

        var execution = await Run(rule, store);

        execution.Status.ShouldBe(RuleStatus.Passed);
        execution.FromCache.ShouldBeFalse();
        rule.Executions.ShouldBe(1);
        store.Writes.ShouldBe(0);
    }

    /// <summary>
    /// A fingerprint that overruns the rule's timeout is a timeout.
    /// </summary>
    /// <remarks>
    /// The fingerprint is the rule's own code and runs under the same deadline.
    /// A rule that hangs while describing its inputs has hung, and letting it
    /// wait outside the timeout would make the timeout advisory for anybody who
    /// implements the interface.
    /// </remarks>
    [Fact]
    public async Task RunAsync_WhenTheFingerprintOverrunsTheTimeout_IsErroredAndNeverExecutes()
    {
        var rule = FakeCacheableRule.Hanging("core.a.alpha");

        var execution = await new RuleRunner(TimeProvider.System, CacheFor(new RecordingCacheStore(), rule))
            .RunAsync(
                rule,
                SnapshotFor(rule, TimeSpan.FromMilliseconds(20)),
                ContextFor(rule, _loggers),
                TestContext.Current.CancellationToken);

        execution.Status.ShouldBe(RuleStatus.Errored);
        execution.ErrorDetail.ShouldNotBeNull().ShouldContain("timed out");
        rule.Executions.ShouldBe(0);
    }

    /// <remarks>
    /// Cancellation during the fingerprint is a cancelled rule, not a timed-out
    /// one and not an unhandled exception escaping the runner. The concurrency contract says
    /// a cancelled run reports what it got to do.
    /// </remarks>
    [Fact]
    public async Task RunAsync_WhenTheRunIsCancelledDuringTheFingerprint_IsCancelled()
    {
        var rule = FakeCacheableRule.Hanging("core.a.alpha");

        using var cancellation = new CancellationTokenSource();

        var running = new RuleRunner(TimeProvider.System, CacheFor(new RecordingCacheStore(), rule))
            .RunAsync(rule, SnapshotFor(rule), ContextFor(rule, _loggers), cancellation.Token);

        await rule.FingerprintStarted.Task;
        await cancellation.CancelAsync();

        var execution = await running;

        execution.Status.ShouldBe(RuleStatus.Errored);
        execution.ErrorDetail.ShouldNotBeNull().ShouldContain("cancelled");
        rule.Executions.ShouldBe(0);
    }

    /// <summary>
    /// A hit replays the outcome and re-derives everything policy owns.
    /// </summary>
    /// <remarks>
    /// The cache key stores a <c>RuleOutcome</c> — a status and its findings —
    /// and nothing else. <c>EffectiveSeverity</c>, <c>Blocking</c> and
    /// <c>Gating</c> come from the snapshot taken for <em>this</em> run, because
    /// An execution records the policy that was in force
    /// when it happened. A cache that replayed them would report last week's
    /// policy as if it were today's, and the instrumentation's whole claim to be
    /// auditable rests on that not happening.
    /// </remarks>
    [Fact]
    public async Task RunAsync_ForAStoredResult_RederivesThePolicyRatherThanReplayingIt()
    {
        var rule = FakeCacheableRule.Describing("core.a.alpha");
        var store = new RecordingCacheStore();

        await Seed(store, rule, RuleOutcome.Failed(new Finding { Message = "stored" }));

        var relaxed = SnapshotFor(rule) with
        {
            Blocking = false,
            Gating = false,
            EffectiveSeverity = Severity.Warning,
        };

        var execution = await new RuleRunner(TimeProvider.System, CacheFor(store, rule)).RunAsync(
            rule, relaxed, ContextFor(rule, _loggers), TestContext.Current.CancellationToken);

        execution.FromCache.ShouldBeTrue();
        execution.Status.ShouldBe(RuleStatus.Failed);
        execution.Blocking.ShouldBeFalse();
        execution.Gating.ShouldBeFalse();
        execution.EffectiveSeverity.ShouldBe(Severity.Warning);
    }

    /// <summary>
    /// The duration recorded for a hit is the lookup, not the original run.
    /// </summary>
    /// <remarks>
    /// The cache key draws it as <c>0.0s</c>. Replaying the original duration
    /// would put a number in the report's percentiles for work that did not
    /// happen in this run — and it is the reason <c>fromCache</c> also has to
    /// reach the history, where the same series is built again.
    /// </remarks>
    [Fact]
    public async Task RunAsync_ForACacheHit_RecordsTheLookupDurationNotTheOriginal()
    {
        var rule = FakeCacheableRule.Describing("core.a.alpha");
        var store = new RecordingCacheStore();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero));

        await Seed(store, rule, RuleOutcome.Passed());

        var execution = await new RuleRunner(clock, CacheFor(store, rule)).RunAsync(
            rule, SnapshotFor(rule), ContextFor(rule, _loggers), TestContext.Current.CancellationToken);

        execution.FromCache.ShouldBeTrue();
        execution.Duration.ShouldBe(TimeSpan.Zero);
    }

    private async Task Seed(RecordingCacheStore store, FakeCacheableRule rule, RuleOutcome outcome)
    {
        var key = await CacheFor(store, rule).KeyForAsync(
            rule, ContextFor(rule, _loggers), TestContext.Current.CancellationToken);

        store.Seed(
            CachePaths.FileFor(Directory, rule.Descriptor.Id, key.ShouldNotBeNull()),
            CachedOutcomeDocument.Serialise(outcome));
    }

    private Task<RuleExecution> Run(FakeCacheableRule rule, RecordingCacheStore store) =>
        new RuleRunner(TimeProvider.System, CacheFor(store, rule)).RunAsync(
            rule, SnapshotFor(rule), ContextFor(rule, _loggers), TestContext.Current.CancellationToken);
}
