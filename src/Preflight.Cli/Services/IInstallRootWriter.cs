namespace Preflight.Cli.Services;

using Preflight.Cli.Pipelines;

/// <summary>
/// Writes inside the install root, and only there.
/// </summary>
/// <remarks>
/// <para>
/// A third writer, beside <c>IWorkspaceFileWriter</c> and
/// <see cref="IMachineStateStore"/>, and none of the three may be merged into
/// another. The workspace writer refuses to replace a file and that refusal is a
/// tested promise; this one replaces a whole version directory, which is what
/// installing the same version twice has to mean if a CI job is allowed to run
/// <c>install</c> on every build.
/// </para>
/// <para>
/// <c>IFileSystem</c> is untouched and declares reads only, so no rule gains
/// anything from any of this. A rule never writes, and the install root sits
/// outside the workspace entirely.
/// </para>
/// </remarks>
public interface IInstallRootWriter
{
    /// <summary>A directory inside the root that nothing else is using.</summary>
    /// <remarks>
    /// Inside the root rather than in the system temporary directory, because
    /// the staged tree is then moved into place and a move across volumes is
    /// neither atomic nor guaranteed to work. <c>PREFLIGHT_HOME</c> on another
    /// drive is an ordinary thing for somebody to do.
    /// </remarks>
    /// <param name="root">The install root.</param>
    DirectoryInfo CreateStaging(PipelineInstallRoot root);

    /// <summary>Writes one file inside a staged tree.</summary>
    /// <param name="stagingRoot">The staging directory.</param>
    /// <param name="relativePath">Where inside it.</param>
    /// <param name="content">What to write.</param>
    void WriteStaged(DirectoryInfo stagingRoot, string relativePath, byte[] content);

    /// <summary>Puts a staged tree in its final place, replacing what was there.</summary>
    /// <param name="stagingRoot">The staged tree.</param>
    /// <param name="destination">Where it belongs.</param>
    void Commit(DirectoryInfo stagingRoot, DirectoryInfo destination);

    /// <summary>Removes a tree, staged or installed.</summary>
    /// <param name="directory">The tree.</param>
    void Remove(DirectoryInfo directory);
}
