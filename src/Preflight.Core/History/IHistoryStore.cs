namespace Preflight.Core.History;

/// <summary>
/// Where a history line is appended.
/// </summary>
/// <remarks>
/// <para>
/// A new interface rather than a member on
/// <see cref="Preflight.Abstractions.IFileSystem"/>, and the reason is stated
/// in two places already. <c>IFileSystem</c> is read-only by construction,
/// because never writing to the workspace is a non-goal expressed in the type
/// system; and the plugin contract prices a new member on a published interface
/// as a <b>major</b> version, which every plugin then has to be recompiled
/// against. The same argument applies for <c>IProcessRunner</c>.
/// </para>
/// <para>
/// One method, taking a whole line. Everything above it — which file, what the
/// record says, whether it had to be truncated — is decided by pure code that
/// needs no disk.
/// </para>
/// </remarks>
public interface IHistoryStore
{
    /// <summary>
    /// Appends one line, creating the directory if it is not there yet.
    /// </summary>
    /// <param name="filePath">The file, which may not exist.</param>
    /// <param name="line">The record, without its terminator.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task AppendAsync(string filePath, string line, CancellationToken cancellationToken);
}
