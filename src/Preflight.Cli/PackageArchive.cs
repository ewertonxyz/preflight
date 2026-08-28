namespace Preflight.Cli;

using System.IO.Compression;
using Preflight.Core;

/// <summary>
/// Raised when a package archive cannot be used.
/// </summary>
public sealed class PackageArchiveException : ConfigurationLoadException
{
    public PackageArchiveException(string message)
        : base(message)
    {
    }
}

/// <summary>One file inside a package archive.</summary>
/// <param name="RelativePath">Its path inside the package, with forward slashes.</param>
public sealed record PackageEntry(string RelativePath);

/// <summary>One file on its way into a package archive.</summary>
/// <param name="RelativePath">Its path inside the package, with forward slashes.</param>
/// <param name="Content">Its bytes.</param>
public sealed record PackageFile(string RelativePath, byte[] Content);

/// <summary>
/// Reads and writes package archives.
/// </summary>
/// <remarks>
/// A seam because the alternative is asserting on real zip files: "nothing was
/// written outside the version directory" can then only be argued from absence,
/// which is the weaker assertion the create-command tests already turned down in
/// favour of proving the writer was never called. It also keeps a defect in this
/// project distinguishable from a change in the compression library.
/// </remarks>
public interface IPackageArchive
{
    /// <summary>Every entry, in the order the archive stores them.</summary>
    /// <param name="archivePath">The archive.</param>
    IReadOnlyList<PackageEntry> Entries(string archivePath);

    /// <summary>Reads one entry's bytes.</summary>
    /// <param name="archivePath">The archive.</param>
    /// <param name="relativePath">The entry.</param>
    byte[] Read(string archivePath, string relativePath);

    /// <summary>Writes a new archive holding exactly <paramref name="files"/>.</summary>
    /// <remarks>
    /// The bytes are a function of <paramref name="files"/> and nothing else.
    /// Two runs over the same content, on two machines, in two directories,
    /// produce the same archive — which is what makes a checksum published
    /// beside a package worth anything.
    /// </remarks>
    /// <param name="archivePath">Where to write it.</param>
    /// <param name="files">What goes in it.</param>
    void Write(string archivePath, IReadOnlyList<PackageFile> files);
}

/// <summary>The real archive, over <see cref="ZipFile"/>.</summary>
public sealed class PackageArchive : IPackageArchive
{
    /// <inheritdoc />
    public IReadOnlyList<PackageEntry> Entries(string archivePath)
    {
        ArgumentNullException.ThrowIfNull(archivePath);

        using var archive = Open(archivePath);

        return [.. archive.Entries
            .Where(entry => entry.FullName.Length > 0 && !entry.FullName.EndsWith('/'))
            .Select(entry => new PackageEntry(entry.FullName))];
    }

    /// <inheritdoc />
    public byte[] Read(string archivePath, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(archivePath);
        ArgumentNullException.ThrowIfNull(relativePath);

        using var archive = Open(archivePath);

        var entry = archive.GetEntry(relativePath)
            ?? throw new PackageArchiveException(
                $"'{relativePath}' is not in {archivePath}.");

        using var stream = entry.Open();
        using var buffer = new MemoryStream();

        stream.CopyTo(buffer);

        return buffer.ToArray();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Three things are pinned here and every one of them is a real source of
    /// drift. Entry order is ordinal rather than whatever the caller enumerated,
    /// because a file system promises no order and two checkouts of one tree
    /// would otherwise pack differently. Timestamps are the zip epoch rather
    /// than the file's own, because the clock is not part of what a package
    /// says. External attributes are zeroed, because .NET writes the unix mode
    /// into them and the same tree packed on Windows and on Linux would
    /// otherwise disagree by a handful of bytes nobody can see.
    /// </remarks>
    public void Write(string archivePath, IReadOnlyList<PackageFile> files)
    {
        ArgumentNullException.ThrowIfNull(archivePath);
        ArgumentNullException.ThrowIfNull(files);

        using var stream = File.Open(archivePath, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var file in files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            var entry = archive.CreateEntry(file.RelativePath, CompressionLevel.Optimal);

            entry.LastWriteTime = ZipEpoch;
            entry.ExternalAttributes = 0;

            using var content = entry.Open();

            content.Write(file.Content);
        }
    }

    /// <summary>
    /// The oldest timestamp the zip format can express.
    /// </summary>
    /// <remarks>
    /// A constant rather than a clock routed through <c>TimeProvider</c>. The
    /// determinism has to survive somebody replacing the seam in a test, and a
    /// value that comes from a parameter is a value a caller can vary.
    /// </remarks>
    private static readonly DateTimeOffset ZipEpoch =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ZipArchive Open(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            throw new PackageArchiveException($"No package at {archivePath}.");
        }

        try
        {
            return ZipFile.OpenRead(archivePath);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException
            or UnauthorizedAccessException)
        {
            throw new PackageArchiveException(
                $"Could not open {archivePath} as a package: {exception.Message}");
        }
    }
}
