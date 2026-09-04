namespace Preflight.Cli.Storage;

using System.IO.Compression;
using Preflight.Cli.Services;
using Preflight.Core;

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
    /// determinism has to survive somebody substituting the clock in a test,
    /// and a value that comes from a parameter is a value a caller can vary.
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
