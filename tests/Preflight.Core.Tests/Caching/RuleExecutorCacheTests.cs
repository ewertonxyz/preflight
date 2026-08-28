namespace Preflight.Core.Tests.Caching;

using NSubstitute;
using Preflight.Abstractions;
using Preflight.Core;
using Preflight.Core.Caching;
using Preflight.Core.Policy;
using Preflight.Core.Tests.Execution;

/// <summary>
/// Where the executor puts the cache, and when it does not have one.
/// </summary>
public sealed class RuleExecutorCacheTests
{
    private readonly RecordingRuleLoggerFactory _loggers = new();

    /// <remarks>
    /// <c>--no-cache</c> is the CLI declining to hand over a store, so the
    /// engine has one condition rather than a flag it might forget to honour in
    /// one of two code paths.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithoutACacheStore_NeitherReadsNorWrites()
    {
        var rule = FakeCacheableRule.Describing("core.a.alpha");
        var store = new RecordingCacheStore();

        await Execute(rule, cache: null);

        store.Reads.ShouldBe(0);
        store.Writes.ShouldBe(0);
        rule.Fingerprints.ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithACacheStore_StoresTheResult()
    {
        var rule = FakeCacheableRule.Describing("core.a.alpha");
        var store = new RecordingCacheStore();

        var result = await Execute(rule, store);

        result.Executions.ShouldHaveSingleItem().FromCache.ShouldBeFalse();
        store.Entries.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ExecuteAsync_TwiceOverTheSameStore_ReportsTheSecondAsCached()
    {
        var rule = FakeCacheableRule.Describing("core.a.alpha");
        var store = new RecordingCacheStore();

        await Execute(rule, store);

        var second = await Execute(rule, store);

        second.Executions.ShouldHaveSingleItem().FromCache.ShouldBeTrue();
        rule.Executions.ShouldBe(1);
    }

    /// <summary>
    /// The cache goes where <c>cachePath</c> says, not where the engine assumes.
    /// </summary>
    /// <remarks>
    /// The key is a hash, so the directory is the only part of the path a test
    /// can read back — and it is the part policy owns. The policy schema lists
    /// <c>cachePath</c> beside <c>historyPath</c>, and the two behaving
    /// differently would be a trap.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_PutsTheEntryUnderTheConfiguredCachePath()
    {
        var rule = FakeCacheableRule.Describing("core.a.alpha");
        var store = new RecordingCacheStore();

        await Execute(rule, store, cachePath: "build/cache");

        store.Entries.Keys.ShouldHaveSingleItem().ShouldBe(Path.Combine(
            CacheFixture.Workspace.FullName,
            "build/cache",
            rule.Descriptor.Id.Value,
            Path.GetFileName(store.Entries.Keys.Single())));
    }

    /// <summary>
    /// A warm entry does not let a rule past the graph.
    /// </summary>
    /// <remarks>
    /// The cache answers "what did this rule conclude", never "should this rule
    /// run". The load-time flow decides the second, and a hit that bypassed it would
    /// report a compile probe's stored result in a run where the configuration
    /// it depends on had just failed — the expensive check reporting green
    /// underneath a red one, which is precisely the arrangement the dependency
    /// graph exists to prevent.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenAGatingDependencyFails_SkipsTheDependentEvenWithAWarmEntry()
    {
        var gate = FakeRule.Failing("core.a.gate");
        var dependent = FakeCacheableRule.Describing("core.a.dependent", dependsOn: "core.a.gate");
        var store = new RecordingCacheStore();

        var policy = EffectivePolicy.Build(
            [gate.Descriptor, dependent.Descriptor],
            pipeline: null,
            local: null,
            setOverrides: [], target: StatedBuildTarget.Unstated);

        var warm = new RuleCache(store, CacheFixture.Directory, policy);

        var key = await warm.KeyForAsync(
            dependent,
            CacheFixture.ContextFor(dependent, _loggers),
            TestContext.Current.CancellationToken);

        store.Seed(
            CachePaths.FileFor(CacheFixture.Directory, dependent.Descriptor.Id, key.ShouldNotBeNull()),
            CachedOutcomeDocument.Serialise(RuleOutcome.Passed()));

        var result = await new RuleExecutor(_loggers, TimeProvider.System).ExecuteAsync(
            new RunRequest
            {
                Rules = [gate, dependent],
                Policy = policy,
                Stage = gate.Descriptor.Stage,
                Target = new BuildTarget("x64", "Debug"),
                WorkspaceRoot = CacheFixture.Workspace,
                FileSystem = Substitute.For<IFileSystem>(),
                Processes = Substitute.For<IProcessRunner>(),
                Cache = store,
                RunId = RunFixture.FixedRunId,
            },
            TestContext.Current.CancellationToken);

        var skipped = result.Executions.Single(execution => execution.RuleId == dependent.Descriptor.Id);

        skipped.Status.ShouldBe(RuleStatus.Skipped);
        skipped.FromCache.ShouldBeFalse();
        skipped.SkippedBecauseOf.ShouldHaveSingleItem().ShouldBe(gate.Descriptor.Id);
        dependent.Executions.ShouldBe(0);
    }

    /// <summary>
    /// A contrast run neither reads nor fills the cache.
    /// </summary>
    /// <remarks>
    /// <c>--no-skip</c> is a run whose purpose is to see what every
    /// rule says right now, so a stored answer defeats it — and, worse, such a
    /// run executes rules whose gating dependency failed, producing exactly the
    /// results the graph exists to stop anybody relying on. Letting those fill
    /// the cache would leak them into ordinary runs.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithNoSkip_NeitherReadsNorWritesTheCache()
    {
        var rule = FakeCacheableRule.Describing("core.a.alpha");
        var store = new RecordingCacheStore();

        await Execute(rule, store, noSkip: true);

        store.Reads.ShouldBe(0);
        store.Writes.ShouldBe(0);
        rule.Fingerprints.ShouldBe(0);
    }

    private Task<RunResult> Execute(
        FakeCacheableRule rule,
        RecordingCacheStore? cache,
        string? cachePath = null,
        bool noSkip = false) =>
        new RuleExecutor(_loggers, TimeProvider.System).ExecuteAsync(
            new RunRequest
            {
                Rules = [rule],
                Policy = Policy(rule, cachePath),
                Stage = rule.Descriptor.Stage,
                Target = new BuildTarget("x64", "Debug"),
                WorkspaceRoot = CacheFixture.Workspace,
                FileSystem = Substitute.For<IFileSystem>(),
                Processes = Substitute.For<IProcessRunner>(),
                Cache = cache,
                NoSkip = noSkip,
                RunId = RunFixture.FixedRunId,
            },
            TestContext.Current.CancellationToken);

    private static EffectivePolicy Policy(FakeCacheableRule rule, string? cachePath) => EffectivePolicy.Build(
        [rule.Descriptor],
        cachePath is null
            ? null
            : PolicyDocument.Parse(
                $$"""{ "schemaVersion": 1, "cachePath": "{{cachePath}}" }""",
                "preflight.base.json"),
        local: null,
        setOverrides: [], target: StatedBuildTarget.Unstated);
}
