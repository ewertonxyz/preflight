namespace Preflight.Cli;

/// <summary>
/// Writes inside the install root, and only there.
/// </summary>
/// <remarks>
/// <para>
/// A third write seam, beside <c>IWorkspaceFileWriter</c> and
/// <see cref="IMachineStateStore"/>, and none of the three may be merged into
/// another. The workspace writer refuses to replace a file and that refusal is a
/// tested promise; this one replaces a whole version directory, which is what
/// installing the same version twice has to mean if a CI job is allowed to run
/// <c>install</c> on every build.
/// </para>
/// <para>
/// <c>IFileSystem</c> is untouched and stays read-only, so no rule gains
/// anything from any of this. That boundary is ADR-028's, and the install root
/// sits outside the workspace entirely.
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

/// <summary>The real install-root writer.</summary>
public sealed class InstallRootWriter : IInstallRootWriter
{
    /// <inheritdoc />
    public DirectoryInfo CreateStaging(PipelineInstallRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var staging = new DirectoryInfo(
            Path.Combine(root.Root.FullName, ".staging", Path.GetRandomFileName()));

        staging.Create();

        return staging;
    }

    /// <inheritdoc />
    public void WriteStaged(DirectoryInfo stagingRoot, string relativePath, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(stagingRoot);
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(content);

        var destination = Path.GetFullPath(Path.Combine(stagingRoot.FullName, relativePath));

        // The second half of the zip-slip guard. The installer checks the entry
        // name before it gets here; this checks the resolved path afterwards,
        // because the two can disagree — a name that looks harmless still
        // resolves outside once the file system has had its say about separators
        // and short names.
        if (!destination.StartsWith(
            Path.TrimEndingDirectorySeparator(stagingRoot.FullName) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new PackageArchiveException(
                $"'{relativePath}' resolves outside the package directory.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, content);
    }

    /// <inheritdoc />
    public void Commit(DirectoryInfo stagingRoot, DirectoryInfo destination)
    {
        ArgumentNullException.ThrowIfNull(stagingRoot);
        ArgumentNullException.ThrowIfNull(destination);

        Directory.CreateDirectory(destination.Parent!.FullName);

        if (destination.Exists)
        {
            destination.Delete(recursive: true);
        }

        Directory.Move(stagingRoot.FullName, destination.FullName);
    }

    /// <inheritdoc />
    public void Remove(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (directory.Exists)
        {
            directory.Delete(recursive: true);
        }
    }
}
