namespace Preflight.Cli.Services;

using System.IO.Compression;
using Preflight.Cli.Storage;
using Preflight.Core;

/// <summary>
/// Reads and writes package archives.
/// </summary>
/// <remarks>
/// Injected because the alternative is asserting on real zip files: "nothing
/// was
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
