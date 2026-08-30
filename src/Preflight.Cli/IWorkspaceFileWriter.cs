namespace Preflight.Cli;

using System.Text;

/// <summary>
/// Creates a file in the workspace, and refuses to replace one.
/// </summary>
/// <remarks>
/// <para>
/// The only write this tool performs inside a workspace, and a type of its own
/// rather than a method on <c>IFileSystem</c>. That contract reads and never
/// writes, and a copy of it is handed to every rule that runs — adding a write
/// to it so that one command could use it would hand the capability to all of
/// them at the same time.
/// </para>
/// <para>
/// No rule ever repairs what it finds, which is what keeps a validation run
/// from quietly changing the workspace it is judging. That prohibition is about
/// a rule correcting its own finding; a command somebody typed asking for a
/// file to be written is the person applying the correction, which is the other
/// half of the same rule. Keeping the boundary here means a rule that wanted to
/// write would have to gain access to a type it is not given — a change visible
/// in a review, rather than one more member on a contract it already holds.
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
