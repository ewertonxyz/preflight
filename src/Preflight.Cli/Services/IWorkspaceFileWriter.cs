namespace Preflight.Cli.Services;

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
