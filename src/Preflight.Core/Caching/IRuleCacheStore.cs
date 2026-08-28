namespace Preflight.Core.Caching;

/// <summary>
/// Where a cached result is read from and written to.
/// </summary>
/// <remarks>
/// A new interface rather than members on
/// <see cref="Preflight.Abstractions.Services.IFileSystem"/>, for the reason that
/// interface states in its own remarks: it is read-only by construction,
/// because the rule that this tool never writes to the workspace is expressed
/// in the type system. A new member on it would also be a major version of the
/// contract. <c>IHistoryStore</c> is the precedent, and this is the same shape.
/// </remarks>
public interface IRuleCacheStore
{
    /// <summary>
    /// The stored result, or <see langword="null"/> when there is none.
    /// </summary>
    Task<string?> ReadAsync(string filePath, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a result, creating the directory if it is not there yet.
    /// </summary>
    Task WriteAsync(string filePath, string content, CancellationToken cancellationToken);

    /// <summary>
    /// Removes every stored result under <paramref name="directory"/>.
    /// </summary>
    /// <returns>How many were removed, which is what the command prints.</returns>
    int Clear(string directory);
}
