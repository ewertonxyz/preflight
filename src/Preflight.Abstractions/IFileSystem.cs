namespace Preflight.Abstractions;

/// <summary>
/// Read-only access to the workspace.
/// </summary>
/// <remarks>
/// Read-only by construction: the rule that this tool never writes to the
/// workspace is expressed in the type system rather than in a comment. Rules at
/// the same topological level run in parallel, and shared writes would be a
/// race waiting to happen.
/// </remarks>
public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    long GetFileSize(string path);

    Stream OpenRead(string path);

    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);

    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);

    IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
}
