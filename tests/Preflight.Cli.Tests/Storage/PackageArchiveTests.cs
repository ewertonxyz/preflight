namespace Preflight.Cli.Tests.Storage;

using System.IO.Compression;
using System.Text;
using Preflight.Cli.Storage;

/// <summary>
/// The one type that knows the zip format, on its own.
/// </summary>
/// <remarks>
/// Everything the packager and the installer do goes through this type, and both
/// of them are tested against whole packages. What that leaves is the handful of
/// answers it gives when it is asked for something that is not there —
/// an archive at a path with no file, a file that is not an archive, an entry
/// the caller named and the archive does not hold. Each of those is a message
/// somebody reads while trying to work out why an install failed, and each was
/// reachable only by accident until now.
/// </remarks>
public sealed class PackageArchiveTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("preflight-archive-");
    private readonly PackageArchive _archive = new();

    public void Dispose() => _root.Delete(recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_root.FullName, name);

    private string Written(string name, params (string Entry, string Content)[] entries)
    {
        var path = Path(name);

        _archive.Write(
            path,
            [.. entries.Select(entry =>
                new PackageFile(entry.Entry, Encoding.UTF8.GetBytes(entry.Content)))]);

        return path;
    }

    [Fact]
    public void Entries_ListsWhatWasWrittenInOrdinalOrder() =>
        _archive.Entries(Written("ordered.zip", ("b.json", "b"), ("a.json", "a"), ("c/d.json", "d")))
            .Select(entry => entry.RelativePath)
            .ShouldBe(["a.json", "b.json", "c/d.json"]);

    [Fact]
    public void Read_ReturnsTheBytesThatWereWritten() =>
        Encoding.UTF8.GetString(_archive.Read(Written("round.zip", ("a.json", "content")), "a.json"))
            .ShouldBe("content");

    /// <remarks>
    /// A directory entry is a name ending in a slash carrying nothing, and a zip
    /// writer that creates one is doing something normal. Listing it as a file
    /// would make the installer look for a digest that no packer would ever have
    /// written, and refuse a package that is fine.
    /// </remarks>
    [Fact]
    public void Entries_SkipsTheDirectoryEntriesAZipMayCarry()
    {
        var path = Path("with-directories.zip");

        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            archive.CreateEntry("rules/");
            archive.CreateEntry("rules/acme.dll");
        }

        _archive.Entries(path).Select(entry => entry.RelativePath).ShouldBe(["rules/acme.dll"]);
    }

    [Fact]
    public void Read_ForAnEntryTheArchiveDoesNotHold_RefusesNamingIt() =>
        Should.Throw<PackageArchiveException>(
            () => _archive.Read(Written("present.zip", ("a.json", "a")), "absent.json"))
            .Message.ShouldContain("absent.json");

    [Fact]
    public void Entries_ForAPathWithNoFile_RefusesNamingIt()
    {
        var absent = Path("never-written.zip");

        Should.Throw<PackageArchiveException>(() => _archive.Entries(absent))
            .Message.ShouldContain(absent);
    }

    [Fact]
    public void Entries_ForAFileThatIsNotAnArchive_RefusesRatherThanThrowingTheLibrarysException()
    {
        var path = Path("not-a-zip.zip");

        File.WriteAllText(path, "this is prose, not a package");

        Should.Throw<PackageArchiveException>(() => _archive.Entries(path))
            .Message.ShouldContain(path);
    }

    /// <remarks>
    /// Never replaces. The packager checks the destination first and produces the
    /// message worth reading; this is what makes the promise true when another
    /// process creates the file between that check and this write.
    /// </remarks>
    [Fact]
    public void Write_OverAPathSomethingAlreadyOccupies_Throws()
    {
        var path = Written("taken.zip", ("a.json", "a"));

        Should.Throw<IOException>(() => _archive.Write(path, []));
    }

    [Fact]
    public void EveryEntryPoint_WithoutItsArgument_Throws()
    {
        Should.Throw<ArgumentNullException>(() => _archive.Entries(null!));
        Should.Throw<ArgumentNullException>(() => _archive.Read(null!, "a"));
        Should.Throw<ArgumentNullException>(() => _archive.Read("a", null!));
        Should.Throw<ArgumentNullException>(() => _archive.Write(null!, []));
        Should.Throw<ArgumentNullException>(() => _archive.Write(Path("x.zip"), null!));
    }
}
