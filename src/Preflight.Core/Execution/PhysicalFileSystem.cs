namespace Preflight.Core.Execution;

using Preflight.Abstractions.Services;

/// <summary>
/// Reads the real disk.
/// </summary>
/// <remarks>
/// <para>
/// The one implementation of <see cref="IFileSystem"/> that ships. Lives in
/// <c>Preflight.Core</c> because the integration tests run the built-in rules
/// against <c>fixtures/workspace-good</c> on real disk, from
/// <c>Preflight.Rules.Tests</c>, and a test project cannot reference an
/// executable. Putting it in the CLI would have produced a second
/// implementation written inside the test project, leaving the shipped one
/// uncovered.
/// </para>
/// <para>
/// Read-only, because the interface is. This tool never writes to the
/// workspace, and rules at the same level run concurrently — the type system is
/// where that is enforced, not a comment on each rule.
/// </para>
/// <para>
/// Deliberately thin: every member is one BCL call. Anything cleverer here —
/// caching, path rewriting, normalisation — would be behaviour that only the
/// production path has and that every rule test, running against a substitute,
/// would never see.
/// </para>
/// </remarks>
public sealed class PhysicalFileSystem : IFileSystem
{
    // These two are guarded and the rest are not, and the difference is what a
    // null actually does. File.Exists and Directory.Exists answer false for a
    // null path — "there is no such file" about a question nobody asked — and a
    // rule reading that would report a finding on evidence it never had. Every
    // other member here reaches a BCL call that already throws
    // ArgumentNullException naming the parameter, and a guard in front of it
    // would be a line that changes nothing.
    public bool FileExists(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return File.Exists(path);
    }

    public bool DirectoryExists(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Directory.Exists(path);
    }

    public long GetFileSize(string path) => new FileInfo(path).Length;

    public Stream OpenRead(string path) => File.OpenRead(path);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(path, cancellationToken);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(path, cancellationToken);

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) =>
        Directory.EnumerateFiles(path, searchPattern, searchOption);
}
