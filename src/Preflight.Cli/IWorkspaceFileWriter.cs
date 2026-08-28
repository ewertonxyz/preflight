namespace Preflight.Cli;

using System.Text;

/// <summary>
/// Creates a file in the workspace, and refuses to replace one.
/// </summary>
/// <remarks>
/// <para>
/// The only write this tool performs inside a workspace, and a seam of its own
/// rather than a method on <c>IFileSystem</c>. That contract is read-only by
/// construction (<c>Docs/design.md 5.5</c>), and every rule holds one — opening
/// a write on it to serve a command that is not a rule would hand the capability
/// to all of them.
/// </para>
/// <para>
/// The non-objective in <c>Docs/design.md 2</c> reads "Nenhuma regra corrige
/// nada": it governs rules repairing their own findings, not a command the user
/// typed. See ADR-028.
/// </para>
/// </remarks>
public interface IWorkspaceFileWriter
{
    /// <summary>Whether something already occupies <paramref name="path"/>.</summary>
    /// <remarks>
    /// A directory or a symlink counts. This answers "may I write here", not
    /// "is there a regular file here", because every occupant is a reason to
    /// stop.
    /// </remarks>
    bool Exists(string path);

    /// <summary>Writes <paramref name="content"/> to a path nothing occupies.</summary>
    /// <exception cref="IOException">
    /// Something already occupies <paramref name="path"/>, or the write failed.
    /// </exception>
    Task WriteNewAsync(string path, string content, CancellationToken cancellationToken);
}

/// <summary>
/// The real writer.
/// </summary>
/// <remarks>
/// Staging file then move, as <c>FileRuleCacheStore</c> does, and for a sharper
/// reason here: a manifest truncated halfway still parses, because the format
/// allows comments and trailing commas, and a truncated manifest declaring no
/// tools is indistinguishable from one somebody meant to leave empty.
/// </remarks>
public sealed class WorkspaceFileWriter : IWorkspaceFileWriter
{
    /// <inheritdoc />
    public bool Exists(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return File.Exists(path) || Directory.Exists(path);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The move is what guarantees the refusal, not the <see cref="Exists"/>
    /// check the caller made first: between that check and this write another
    /// process can create the file, and <see cref="File.Move(string, string)"/>
    /// without <c>overwrite</c> throws rather than replacing it. The check
    /// exists to produce a message worth reading; this line is what makes the
    /// promise true.
    /// </remarks>
    public async Task WriteNewAsync(string path, string content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(content);

        var directory = Path.GetDirectoryName(path)!;
        var staging = Path.Combine(directory, Path.GetRandomFileName());

        await File.WriteAllTextAsync(staging, content, Encoding.UTF8, cancellationToken);

        try
        {
            File.Move(staging, path);
        }
        catch
        {
            // Removed rather than left behind, for the reason FileRuleCacheStore
            // gives: a directory filling with abandoned temporary files is the
            // sort of thing somebody finds a year later and cannot attribute.
            File.Delete(staging);

            throw;
        }
    }
}
