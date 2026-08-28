namespace Preflight.Abstractions.Services;

/// <summary>
/// Read-only access to the workspace.
/// </summary>
/// <remarks>
/// <para>
/// The rule that this tool never writes to the workspace is expressed in the
/// type system rather than in a comment: there is no member here that could
/// write, so a rule that wanted to would not compile. Rules at the same
/// topological level run in parallel, and shared writes would be a race
/// waiting to happen.
/// </para>
/// <para>
/// Every member that reads a path throws when the path is not there, exactly
/// as the BCL call underneath it does — nothing here converts a missing file
/// into a zero or an empty string. An escaping exception becomes an errored
/// rule, so a rule that is not certain a path exists asks
/// <see cref="FileExists(string)"/> or <see cref="DirectoryExists(string)"/>
/// first. That is not defensiveness for its own sake: a pre-submit rule is
/// handed deleted files among the changed ones, and asking a deleted file for
/// its size is the ordinary case, not the strange one.
/// </para>
/// <para>
/// Paths are taken as given and never rewritten. A rule combines
/// <c>RuleContext.WorkspaceRoot</c> with the relative path it was handed and
/// passes the result; normalising here would be behaviour only the real disk
/// has, which every rule tested against a substitute would never see.
/// </para>
/// </remarks>
public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    /// <summary>Size in bytes. Throws when the file is not there.</summary>
    long GetFileSize(string path);

    /// <summary>Opens the file for reading. Throws when it is not there.</summary>
    Stream OpenRead(string path);

    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);

    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Walks a directory. Throws when the directory is not there, and streams
    /// rather than materialising — a content tree can hold millions of entries,
    /// and a rule that stops early should pay only for what it read.
    /// </summary>
    IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
}
