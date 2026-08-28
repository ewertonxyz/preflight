namespace Preflight.Core.Caching;

using System.Text;

/// <summary>
/// Reads and writes cached results as real files.
/// </summary>
/// <remarks>
/// <para>
/// A missing file is <see langword="null"/> rather than an exception: a cold
/// cache is the ordinary state, not a fault, and the whole mechanism has to be
/// invisible when it is empty.
/// </para>
/// <para>
/// The write goes to a temporary name in the same directory and is then moved
/// into place. A cache entry half-written by a process that was killed would
/// otherwise be a truncated JSON document that every later run reads, fails to
/// parse, and pays for — and unlike the history, where a damaged line is
/// skipped and counted, a damaged cache entry has no reader that reports it to
/// anybody.
/// </para>
/// <para>
/// Losing the move is not a failure. Two runs of the same workspace compute the
/// same key by construction, so a writer that finds the destination taken has
/// been beaten to it by somebody writing the same bytes — and on Windows that
/// race surfaces as an <c>UnauthorizedAccessException</c> rather than as
/// anything that reads like a race. Any other cause is simply a cache entry
/// nobody gets, which costs one execution.
/// </para>
/// </remarks>
public sealed class FileRuleCacheStore : IRuleCacheStore
{
    /// <inheritdoc />
    public async Task<string?> ReadAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
    }

    /// <inheritdoc />
    public async Task WriteAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(content);

        var directory = Path.GetDirectoryName(filePath)!;

        // Unconditional: CreateDirectory is idempotent and already handles two
        // processes racing to create the same directory.
        Directory.CreateDirectory(directory);

        var staging = Path.Combine(directory, Path.GetRandomFileName());

        await File.WriteAllTextAsync(staging, content, Encoding.UTF8, cancellationToken);

        try
        {
            File.Move(staging, filePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The staging file is removed rather than left behind. A directory
            // slowly filling with abandoned temporary files is the sort of thing
            // somebody finds a year later and cannot attribute to anything.
            File.Delete(staging);
        }
    }

    /// <inheritdoc />
    public int Clear(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var files = Directory.GetFiles(directory, CachePaths.SearchPattern, SearchOption.AllDirectories);

        foreach (var file in files)
        {
            File.Delete(file);
        }

        return files.Length;
    }
}
