namespace Preflight.Cli.Services;

using Preflight.Cli.Pipelines;

/// <summary>
/// Reads and replaces the machine state file.
/// </summary>
/// <remarks>
/// <para>
/// A second writer, and deliberately not <c>IWorkspaceFileWriter</c>. That
/// one refuses to replace a file, and the <c>File.Move</c> without a flag
/// <em>is</em> the promise rather than a detail of it — which is exactly right
/// for a manifest somebody authored and exactly wrong for a pin whose whole
/// purpose is to be changed. Two opposite rules in one codebase, and a test on
/// each pins the opposite outcome so that a later "unification" breaks loudly.
/// </para>
/// <para>
/// It is also outside the workspace, so the boundary a rule lives inside is
/// untouched: <c>IFileSystem</c> declares reads only and no rule gains
/// anything.
/// </para>
/// </remarks>
public interface IMachineStateStore
{
    /// <summary>
    /// Reads the state, or refuses a file it cannot understand.
    /// </summary>
    /// <remarks>
    /// An absent file is <see cref="MachineState.Empty"/> and not an error — a
    /// machine that has installed nothing is a normal machine. A file that
    /// exists and cannot be read is a refusal naming it, never a silent reset:
    /// resetting drops every pin, and the next run uses the newest installed
    /// version instead of the pinned one with nothing printed about it.
    /// </remarks>
    /// <param name="path">Where the file lives.</param>
    MachineState Read(string path);

    /// <summary>Replaces the state at <paramref name="path"/>.</summary>
    /// <param name="path">Where the file lives.</param>
    /// <param name="state">What to write.</param>
    void Write(string path, MachineState state);
}
