namespace Preflight.Core.Tests.Caching;

using Preflight.Core.Caching;

/// <summary>
/// The cache on a real disk.
/// </summary>
public sealed class FileRuleCacheStoreTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("preflight-cache-");
    private readonly FileRuleCacheStore _store = new();

    public void Dispose() => _root.Delete(recursive: true);

    /// <remarks>
    /// A cold cache is the ordinary state, not a fault. The whole mechanism has
    /// to be invisible when it is empty.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_ForAnEntryThatIsNotThere_IsNull() =>
        (await _store.ReadAsync(Entry("missing"), TestContext.Current.CancellationToken)).ShouldBeNull();

    [Fact]
    public async Task WriteAsync_ThenRead_ReturnsWhatWasWritten()
    {
        await _store.WriteAsync(Entry("abc"), """{"status":"Passed"}""", TestContext.Current.CancellationToken);

        (await _store.ReadAsync(Entry("abc"), TestContext.Current.CancellationToken))
            .ShouldBe("""{"status":"Passed"}""");
    }

    [Fact]
    public async Task WriteAsync_IntoADirectoryThatDoesNotExist_CreatesIt()
    {
        var nested = Path.Combine(_root.FullName, "core.build.compile-probe", "abc.json");

        await _store.WriteAsync(nested, "{}", TestContext.Current.CancellationToken);

        File.Exists(nested).ShouldBeTrue();
    }

    /// <remarks>
    /// The staging file is moved into place rather than written in situ, so a
    /// process killed mid-write cannot leave a truncated entry that every later
    /// run reads and pays for. Unlike a damaged history line, which the history format
    /// has a reader that counts, a damaged cache entry is reported to nobody.
    /// </remarks>
    [Fact]
    public async Task WriteAsync_LeavesNoStagingFileBehind()
    {
        await _store.WriteAsync(Entry("abc"), "{}", TestContext.Current.CancellationToken);

        Directory.GetFiles(_root.FullName).Select(Path.GetFileName).ShouldBe(["abc.json"]);
    }

    [Fact]
    public async Task WriteAsync_OverAnExistingEntry_ReplacesIt()
    {
        await _store.WriteAsync(Entry("abc"), "first", TestContext.Current.CancellationToken);
        await _store.WriteAsync(Entry("abc"), "second", TestContext.Current.CancellationToken);

        (await _store.ReadAsync(Entry("abc"), TestContext.Current.CancellationToken)).ShouldBe("second");
    }

    /// <remarks>
    /// The count is what <c>preflight cache clear</c> prints, and an already
    /// empty cache and one that just lost four hundred entries are different
    /// facts worth telling apart.
    /// </remarks>
    [Fact]
    public async Task Clear_RemovesEveryEntryUnderEveryRuleAndCountsThem()
    {
        await _store.WriteAsync(
            Path.Combine(_root.FullName, "core.a.alpha", "one.json"), "{}", TestContext.Current.CancellationToken);
        await _store.WriteAsync(
            Path.Combine(_root.FullName, "core.a.bravo", "two.json"), "{}", TestContext.Current.CancellationToken);

        _store.Clear(_root.FullName).ShouldBe(2);

        Directory.GetFiles(_root.FullName, "*.json", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public void Clear_ForACacheThatWasNeverWritten_IsZero() =>
        _store.Clear(Path.Combine(_root.FullName, "never")).ShouldBe(0);

    /// <summary>
    /// An entry that cannot be put in place costs a lookup, not a run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provoked deterministically, by making the destination a directory, rather
    /// than by racing two writers. The race is the reason the branch exists —
    /// two runs of the same workspace compute the same key, and on Windows the
    /// loser gets an <c>UnauthorizedAccessException</c> — but a test that has to
    /// win a race to cover a branch is one that stops covering it on a quieter
    /// machine, which is the same objection <c>ProcessRunner.Kill</c> records.
    /// </para>
    /// <para>
    /// The staging file being gone is half the assertion. A directory slowly
    /// filling with abandoned temporary files is the sort of thing somebody
    /// finds a year later and cannot attribute to anything.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task WriteAsync_WhenTheEntryCannotBePutInPlace_LeavesNothingBehind()
    {
        var occupied = Path.Combine(_root.FullName, "abc.json");

        Directory.CreateDirectory(occupied);

        await Should.NotThrowAsync(() =>
            _store.WriteAsync(occupied, "{}", TestContext.Current.CancellationToken));

        Directory.GetFiles(_root.FullName).ShouldBeEmpty();
    }

    /// <summary>
    /// Many writers onto one key leave one entry that reads.
    /// </summary>
    /// <remarks>
    /// Two runs of the same workspace compute the same key, and rules at one
    /// level run concurrently, so this is the ordinary case rather than an exotic
    /// one. What it asserts is the outcome — one entry, parseable, no leftovers —
    /// and not which writer won, because the key is a hash of the inputs and
    /// every one of them is writing the same bytes.
    /// </remarks>
    [Fact]
    public async Task WriteAsync_FromManyWritersOntoOneKey_LeavesOneEntryThatReads()
    {
        var path = Entry("abc");

        await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            _store.WriteAsync(
                path,
                $"{{\"status\":\"Passed\",\"n\":{index}}}",
                TestContext.Current.CancellationToken)));

        var content = (await _store.ReadAsync(path, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        System.Text.Json.JsonDocument.Parse(content)
            .RootElement.GetProperty("status").GetString().ShouldBe("Passed");

        Directory.GetFiles(_root.FullName).Length.ShouldBe(1);
    }

    /// <summary>
    /// Clearing the cache does not take the history with it.
    /// </summary>
    /// <remarks>
    /// The two directories are siblings under <c>.preflight</c> by default, and
    /// <c>Clear</c> recurses. Losing the history to a cache command would be
    /// losing the only record the instrumentation has, to a command about something else.
    /// </remarks>
    [Fact]
    public async Task Clear_LeavesASiblingHistoryDirectoryAlone()
    {
        var preflight = Directory.CreateDirectory(Path.Combine(_root.FullName, ".preflight"));
        var cache = Path.Combine(preflight.FullName, "cache");
        var history = Directory.CreateDirectory(Path.Combine(preflight.FullName, "history"));

        await _store.WriteAsync(
            Path.Combine(cache, "core.a.alpha", "one.json"), "{}", TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(history.FullName, "2026-08.WKS-1234.ndjson"),
            "{}\n",
            TestContext.Current.CancellationToken);

        _store.Clear(cache).ShouldBe(1);

        File.Exists(Path.Combine(history.FullName, "2026-08.WKS-1234.ndjson")).ShouldBeTrue();
    }

    /// <remarks>
    /// A bare file name has no directory component, and
    /// <c>Directory.CreateDirectory("")</c> throws. Reaching it means the cache
    /// path resolved to something unexpected — worth failing on rather than
    /// papering over, because the alternative is writing entries somewhere
    /// nobody will ever look for them.
    /// </remarks>
    [Fact]
    public async Task WriteAsync_ForAPathWithNoDirectoryComponent_Throws() =>
        await Should.ThrowAsync<ArgumentException>(() =>
            _store.WriteAsync("abc.json", "{}", TestContext.Current.CancellationToken));

    private string Entry(string key) => Path.Combine(_root.FullName, key + ".json");
}
