namespace Preflight.Core.Tests.History;

using System.Text;
using Preflight.Core.History;

/// <summary>
/// The append itself, on a real disk.
/// </summary>
/// <remarks>
/// <para>
/// The history format rests on an assumption it states rather than presumes: a single
/// write on a handle opened for append is serialised by the operating system on
/// a local disk, and has no guarantee at all on a network share. Nothing here
/// tries to prove that assumption. A test that had to win a race against the
/// kernel in order to fail is a test that fails on a loaded machine, which is
/// the same reason <c>ProcessRunner.Kill</c> is excluded from coverage rather
/// than provoked.
/// </para>
/// <para>
/// What is asserted is the half this class owes the assumption: the opening
/// flags, and that many writers produce many well-formed lines rather than one
/// mangled file.
/// </para>
/// </remarks>
public sealed class FileHistoryStoreTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("preflight-history-");

    public void Dispose() => _root.Delete(recursive: true);

    /// <remarks>
    /// <see cref="FileShare.ReadWrite"/> is the load-bearing one. Without it a
    /// second process appending at the same moment is refused the handle, and
    /// <c>preflight report</c> cannot read a file a run is writing to.
    /// </remarks>
    [Fact]
    public void AppendOptions_AreTheOnesSection101Requires()
    {
        FileHistoryStore.AppendOptions.Mode.ShouldBe(FileMode.Append);
        FileHistoryStore.AppendOptions.Access.ShouldBe(FileAccess.Write);
        FileHistoryStore.AppendOptions.Share.ShouldBe(FileShare.ReadWrite);
    }

    /// <remarks>
    /// The history format creates the directory on demand at the first write. A run in
    /// a fresh workspace is the ordinary case, not an error to report.
    /// </remarks>
    [Fact]
    public async Task AppendAsync_IntoADirectoryThatDoesNotExist_CreatesItAndWritesTheLine()
    {
        var path = Path.Combine(_root.FullName, "nested", "deeper", "2026-08.WKS-1234.ndjson");

        await new FileHistoryStore().AppendAsync(path, "{}", TestContext.Current.CancellationToken);

        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ShouldBe("{}\n");
    }

    [Fact]
    public async Task AppendAsync_Twice_AppendsRatherThanTruncating()
    {
        var path = Path.Combine(_root.FullName, "2026-08.WKS-1234.ndjson");
        var store = new FileHistoryStore();

        await store.AppendAsync(path, "{\"first\":1}", TestContext.Current.CancellationToken);
        await store.AppendAsync(path, "{\"second\":2}", TestContext.Current.CancellationToken);

        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))
            .ShouldBe("{\"first\":1}\n{\"second\":2}\n");
    }

    /// <remarks>
    /// Twenty concurrent appends, asserting only that the path supports the use
    /// the history format describes on a local disk. It does not try to provoke
    /// interleaving: see this class's remarks.
    /// </remarks>
    [Fact]
    public async Task AppendAsync_FromManyTasks_ProducesOneWellFormedLinePerTask()
    {
        var path = Path.Combine(_root.FullName, "2026-08.WKS-1234.ndjson");
        var store = new FileHistoryStore();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            store.AppendAsync(path, $"{{\"n\":{index}}}", TestContext.Current.CancellationToken)));

        var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);

        lines.Length.ShouldBe(20);
        lines.Select(line => System.Text.Json.JsonDocument.Parse(line).RootElement.GetProperty("n").GetInt32())
            .Order()
            .ShouldBe(Enumerable.Range(0, 20));
    }

    /// <remarks>
    /// Establishes the contract the substituted store imitates in the CLI tests:
    /// a disk that says no throws, and it is the caller's job to make sure that
    /// does not change the verdict.
    /// </remarks>
    [Fact]
    public async Task AppendAsync_WhereTheDirectoryIsOccupiedByAFile_Throws()
    {
        var occupied = Path.Combine(_root.FullName, "history");

        await File.WriteAllTextAsync(occupied, "not a directory", TestContext.Current.CancellationToken);

        await Should.ThrowAsync<IOException>(() => new FileHistoryStore().AppendAsync(
            Path.Combine(occupied, "2026-08.WKS-1234.ndjson"),
            "{}",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AppendAsync_WritesUtf8()
    {
        var path = Path.Combine(_root.FullName, "2026-08.WKS-1234.ndjson");

        await new FileHistoryStore().AppendAsync(
            path, "{\"m\":\"caf\u00e9\"}", TestContext.Current.CancellationToken);

        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken))
            .ShouldBe(Encoding.UTF8.GetBytes("{\"m\":\"caf\u00e9\"}\n"));
    }
}
