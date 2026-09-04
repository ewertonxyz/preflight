namespace Preflight.Cli.Storage;

using Preflight.Cli.Pipelines;
using Preflight.Cli.Services;

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
