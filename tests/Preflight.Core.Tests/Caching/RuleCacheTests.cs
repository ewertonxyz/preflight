namespace Preflight.Core.Tests.Caching;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core.Caching;
using Preflight.Core.History;
using Preflight.Core.Tests.Execution;
using static Preflight.Core.Tests.Caching.CacheFixture;

/// <summary>
/// The engine's side of the incremental cache: when there is a key, and what may be
/// stored under it.
/// </summary>
/// <remarks>
/// Everything here fails soft except the one thing that must not: it never
/// guesses. The fingerprint contract has no approximate fingerprint, so every path that
/// cannot produce an exact key produces none.
/// </remarks>
public sealed class RuleCacheTests
{
    private readonly RecordingRuleLoggerFactory _loggers = new();

    /// <remarks>
    /// A rule that does not implement the interface is never cached and does not
    /// change by one character — which is the entire argument the fingerprint contract makes
    /// for a separate optional interface over a member on
    /// <see cref="IValidationRule"/>.
    /// </remarks>
    [Fact]
    public async Task KeyForAsync_ForARuleThatIsNotCacheable_IsNull()
    {
        var rule = FakeRule.Passing("core.a.alpha");

        (await KeyFor(new RecordingCacheStore(), rule)).ShouldBeNull();
    }

    [Fact]
    public async Task KeyForAsync_ForARuleThatDeclines_IsNull()
    {
        var rule = FakeCacheableRule.Declining("core.a.alpha");

        (await KeyFor(new RecordingCacheStore(), rule)).ShouldBeNull();

        rule.Fingerprints.ShouldBe(1);
    }

    /// <summary>
    /// A fingerprint that throws is not the rule's verdict.
    /// </summary>
    /// <remarks>
    /// Failing the rule over it would let a defect in an optimisation reject
    /// somebody's workspace. It is reported rather than swallowed, because a
    /// rule whose fingerprint always throws is paying the interface's cost for
    /// none of its benefit and nobody would ever find out.
    /// </remarks>
    [Fact]
    public async Task KeyForAsync_ForARuleWhoseFingerprintThrows_IsNullAndSaysSo()
    {
        var rule = FakeCacheableRule.Breaking("core.a.alpha", "no such directory");

        (await KeyFor(new RecordingCacheStore(), rule)).ShouldBeNull();

        _loggers.MessagesFor(rule.Descriptor.Id)
            .ShouldContain(message => message.Contains("no such directory", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeyForAsync_ForTheSameFingerprint_IsStable()
    {
        var store = new RecordingCacheStore();
        var rule = FakeCacheableRule.Describing("core.a.alpha");

        (await KeyFor(store, rule)).ShouldBe(await KeyFor(store, rule));
    }

    [Fact]
    public async Task KeyForAsync_ForADifferentFingerprint_Differs()
    {
        var store = new RecordingCacheStore();

        (await KeyFor(store, FakeCacheableRule.Describing("core.a.alpha", "aaaa")))
            .ShouldNotBe(await KeyFor(store, FakeCacheableRule.Describing("core.a.alpha", "bbbb")));
    }

    [Fact]
    public async Task TryReadAsync_ForAnEntryThatIsNotThere_IsNull()
    {
        var rule = FakeCacheableRule.Describing("core.a.alpha");

        (await Read(new RecordingCacheStore(), rule, "abc")).ShouldBeNull();
    }

    [Fact]
    public async Task TryReadAsync_ForAStoredOutcome_ReturnsIt()
    {
        var store = new RecordingCacheStore();
        var rule = FakeCacheableRule.Describing("core.a.alpha");

        store.Seed(
            CachePaths.FileFor(Directory, rule.Descriptor.Id, "abc"),
            CachedOutcomeDocument.Serialise(RuleOutcome.Failed(new Finding { Message = "stored" })));

        var outcome = (await Read(store, rule, "abc")).ShouldNotBeNull();

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Message.ShouldBe("stored");
    }

    /// <summary>
    /// An entry holding a status the cache never writes is a miss.
    /// </summary>
    /// <remarks>
    /// It was not written by this code, so trusting it could report a skip
    /// nobody attributed or a crash nobody had. Treating it as a miss costs one
    /// execution.
    /// </remarks>
    [Theory]
    [InlineData(RuleStatus.Errored)]
    [InlineData(RuleStatus.Skipped)]
    public async Task TryReadAsync_ForAnEntryTheCacheWouldNeverHaveWritten_IsAMiss(RuleStatus status)
    {
        var store = new RecordingCacheStore();
        var rule = FakeCacheableRule.Describing("core.a.alpha");

        store.Seed(
            CachePaths.FileFor(Directory, rule.Descriptor.Id, "abc"),
            CachedOutcomeDocument.Serialise(new RuleOutcome { Status = status }));

        (await Read(store, rule, "abc")).ShouldBeNull();
    }

    [Fact]
    public async Task TryReadAsync_ForAnEntryThatWillNotParse_IsAMiss()
    {
        var store = new RecordingCacheStore();
        var rule = FakeCacheableRule.Describing("core.a.alpha");

        store.Seed(CachePaths.FileFor(Directory, rule.Descriptor.Id, "abc"), "{ truncated");

        (await Read(store, rule, "abc")).ShouldBeNull();
    }

    /// <remarks>
    /// A disk that says no means no cache, never a failed run. The cache is an
    /// optimisation, and one that can turn a valid workspace into an error has
    /// traded away the thing the tool is for.
    /// </remarks>
    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task TryReadAsync_WhenTheStoreRefuses_IsNullRatherThanAThrow(Type failure)
    {
        var store = new RecordingCacheStore((Exception)Activator.CreateInstance(failure)!);
        var rule = FakeCacheableRule.Describing("core.a.alpha");

        (await Read(store, rule, "abc")).ShouldBeNull();
    }

    /// <remarks>
    /// The cache contract: a rule that exploded has to explode again. Caching a crash
    /// hides an unstable environment and turns an intermittent problem into a
    /// permanent, wrong result.
    /// </remarks>
    [Theory]
    [InlineData(RuleStatus.Errored)]
    [InlineData(RuleStatus.Skipped)]
    public async Task WriteAsync_ForAnOutcomeTheCacheMayNotStore_WritesNothing(RuleStatus status)
    {
        var store = new RecordingCacheStore();
        var rule = FakeCacheableRule.Describing("core.a.alpha");

        await Write(store, rule, "abc", new RuleOutcome { Status = status });

        store.Writes.ShouldBe(0);
    }

    [Theory]
    [InlineData(RuleStatus.Passed)]
    [InlineData(RuleStatus.Warning)]
    [InlineData(RuleStatus.Failed)]
    [InlineData(RuleStatus.NotApplicable)]
    public async Task WriteAsync_ForAnOutcomeTheCacheMayStore_WritesIt(RuleStatus status)
    {
        var store = new RecordingCacheStore();
        var rule = FakeCacheableRule.Describing("core.a.alpha");

        await Write(store, rule, "abc", new RuleOutcome { Status = status });

        store.Entries.Keys.ShouldHaveSingleItem()
            .ShouldBe(CachePaths.FileFor(Directory, rule.Descriptor.Id, "abc"));
    }

    /// <remarks>
    /// A rule returning null is a contract violation the runner already reports.
    /// Accepting it here keeps the runner to one condition on the path where
    /// both have to agree.
    /// </remarks>
    [Fact]
    public async Task WriteAsync_ForANullOutcome_WritesNothing()
    {
        var store = new RecordingCacheStore();
        var rule = FakeCacheableRule.Describing("core.a.alpha");

        await Write(store, rule, "abc", null);

        store.Writes.ShouldBe(0);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task WriteAsync_WhenTheStoreRefuses_DoesNotPropagate(Type failure)
    {
        var store = new RecordingCacheStore((Exception)Activator.CreateInstance(failure)!);
        var rule = FakeCacheableRule.Describing("core.a.alpha");

        await Should.NotThrowAsync(() => Write(store, rule, "abc", RuleOutcome.Passed()));
    }

    [Fact]
    public void Clear_EmptiesTheStore()
    {
        var store = new RecordingCacheStore();

        store.Seed("one", "{}");
        store.Seed("two", "{}");

        CacheFor(store, FakeCacheableRule.Describing("core.a.alpha"))
            .Clear(Workspace)
            .ShouldBe(2);
    }

    /// <summary>
    /// A cache path that contains the workspace is refused, not emptied.
    /// </summary>
    /// <remarks>
    /// <c>cachePath</c> is a free string that any policy overlay may set. Without
    /// this, <c>"cachePath": "."</c> turns <c>preflight cache clear</c> into a
    /// command that deletes every JSON file in the repository, recursively — a
    /// validation tool destroying the workspace it exists to protect, which is
    /// the worst thing anything in this design could do.
    /// </remarks>
    [Theory]
    [InlineData(".")]
    [InlineData("a/..")]
    [InlineData("..")]
    public void RequireSafeToEmpty_ForAPathThatContainsTheWorkspace_Refuses(string relative) =>
        Should.Throw<UnsafeCachePathException>(() => RuleCache.RequireSafeToEmpty(
                Workspace,
                Path.Combine(Workspace.FullName, relative),
                History))
            .Message.ShouldContain(CacheSettings.PathKey);

    /// <summary>
    /// A cache path that would take the run history with it is refused.
    /// </summary>
    /// <remarks>
    /// The refusal that was documented before it existed. <c>.preflight</c> is
    /// the parent of the default history directory and does not contain the
    /// workspace, so the first check lets it through; the history survived only
    /// because clearing matches the cache's extension and a history file
    /// carries a different one. A coincidence of two constants is not a
    /// guarantee, and losing a month of instrumentation to a cache command is
    /// not a mistake anybody gets to make twice.
    /// </remarks>
    [Theory]
    [InlineData(".preflight")]
    [InlineData(".preflight/history")]
    [InlineData(".preflight/history/..")]
    public void RequireSafeToEmpty_ForAPathThatContainsTheHistory_Refuses(string relative) =>
        Should.Throw<UnsafeCachePathException>(() => RuleCache.RequireSafeToEmpty(
                Workspace,
                Path.Combine(Workspace.FullName, relative),
                History))
            .Message.ShouldContain(HistorySettings.PathKey);

    /// <remarks>
    /// The ordinary shapes stay allowed: the default, which sits beside the
    /// history rather than above it, and one somewhere else entirely. Only
    /// containment downwards is refused — a history directory that contains the
    /// cache loses nothing, because emptying the cache never walks upwards.
    /// </remarks>
    [Theory]
    [InlineData(".preflight/cache")]
    [InlineData("build/cache")]
    public void RequireSafeToEmpty_ForADirectoryOfItsOwn_Allows(string relative) =>
        Should.NotThrow(() => RuleCache.RequireSafeToEmpty(
            Workspace,
            Path.Combine(Workspace.FullName, relative),
            History));

    [Theory]
    [InlineData(RuleStatus.Passed, true)]
    [InlineData(RuleStatus.Warning, true)]
    [InlineData(RuleStatus.Failed, true)]
    [InlineData(RuleStatus.NotApplicable, true)]
    [InlineData(RuleStatus.Errored, false)]
    [InlineData(RuleStatus.Skipped, false)]
    public void IsCacheable_IsTrueForEveryStatusExceptTheEnginesOwn(RuleStatus status, bool expected) =>
        RuleCache.IsCacheable(new RuleOutcome { Status = status }).ShouldBe(expected);

    private Task<string?> KeyFor(RecordingCacheStore store, IValidationRule rule) =>
        CacheFor(store, rule).KeyForAsync(
            rule, ContextFor(rule, _loggers), TestContext.Current.CancellationToken);

    private Task<RuleOutcome?> Read(RecordingCacheStore store, FakeCacheableRule rule, string key) =>
        CacheFor(store, rule).TryReadAsync(
            rule.Descriptor.Id, key, ContextFor(rule, _loggers), TestContext.Current.CancellationToken);

    private Task Write(RecordingCacheStore store, FakeCacheableRule rule, string key, RuleOutcome? outcome) =>
        CacheFor(store, rule).WriteAsync(
            rule.Descriptor.Id, key, outcome, ContextFor(rule, _loggers), TestContext.Current.CancellationToken);
}
