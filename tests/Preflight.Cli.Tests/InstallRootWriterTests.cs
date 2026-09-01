namespace Preflight.Cli.Tests;

using System.Text;

/// <summary>
/// The third write seam, and the one with the opposite promise to the first.
/// </summary>
/// <remarks>
/// <para>
/// <c>IWorkspaceFileWriter</c> refuses to replace a file and that refusal is the
/// tested promise; this one replaces a whole version directory, which is exactly
/// what an idempotent <c>install</c> needs. The two are opposite by decision
/// (ADR-033), and the test on each side is what makes a later "unification"
/// break loudly instead of quietly relaxing one of them —
/// <c>WorkspaceFileWriterTests</c> is the other half, and the two remarks cite
/// each other.
/// </para>
/// <para>
/// The staging directory lives inside the install root rather than in the
/// system temp, and that is not tidiness: <c>File.Move</c> is not atomic across
/// volumes, and <c>PREFLIGHT_HOME</c> pointing at another drive is an ordinary
/// thing for somebody to do.
/// </para>
/// </remarks>
public sealed class InstallRootWriterTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("preflight-install-writer-");
    private readonly InstallRootWriter _writer = new();

    public void Dispose() => _root.Delete(recursive: true);

    private PipelineInstallRoot Root => new(_root);

    [Fact]
    public void CreateStaging_ProducesAnEmptyDirectoryInsideTheInstallRoot()
    {
        var staging = _writer.CreateStaging(Root);

        staging.Exists.ShouldBeTrue();
        staging.EnumerateFileSystemInfos().ShouldBeEmpty();
        staging.FullName.ShouldStartWith(_root.FullName);
    }

    [Fact]
    public void WriteStaged_PutsTheContentWhereTheRelativePathSays()
    {
        var staging = _writer.CreateStaging(Root);

        _writer.WriteStaged(staging, "rules/acme.dll", Encoding.UTF8.GetBytes("payload"));

        File.ReadAllText(Path.Combine(staging.FullName, "rules", "acme.dll")).ShouldBe("payload");
    }

    /// <summary>
    /// A relative path that climbs out of the staging tree is refused here too.
    /// </summary>
    /// <remarks>
    /// The installer already checks the entry name before it gets this far, and
    /// this check is not redundant with it: the two can disagree, because a name
    /// that looks harmless still resolves outside once the file system has had
    /// its say about separators and short names. The check that matters is the
    /// one made on the resolved path, and it is made here because here is where
    /// the resolved path exists.
    /// </remarks>
    [Theory]
    [InlineData("../escaped.dll")]
    [InlineData("rules/../../escaped.dll")]
    public void WriteStaged_WithAPathThatResolvesOutside_RefusesAndWritesNothing(string relativePath)
    {
        var staging = _writer.CreateStaging(Root);

        Should.Throw<PackageArchiveException>(
            () => _writer.WriteStaged(staging, relativePath, [1, 2, 3]))
            .Message.ShouldContain(relativePath);

        _root.EnumerateFiles("*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    /// <remarks>
    /// The opposite of the workspace writer, and the assertion that says so.
    /// Installing the same package twice is what a CI job does on every run, and
    /// a commit that refused to replace would make the second one fail.
    /// </remarks>
    [Fact]
    public void Commit_OverAnExistingVersionDirectory_ReplacesItWholesale()
    {
        var destination = Root.VersionDirectory("projecta", Version("1.4.0"));

        Directory.CreateDirectory(destination.FullName);
        File.WriteAllText(Path.Combine(destination.FullName, "stale.txt"), "from the last install");

        var staging = _writer.CreateStaging(Root);

        _writer.WriteStaged(staging, "fresh.txt", Encoding.UTF8.GetBytes("from this one"));
        _writer.Commit(staging, destination);

        destination.Refresh();
        File.Exists(Path.Combine(destination.FullName, "fresh.txt")).ShouldBeTrue();

        // Replaced, not merged. A file the new version does not carry must not
        // survive from the old one — it would be loaded on the next run as
        // though the package still shipped it.
        File.Exists(Path.Combine(destination.FullName, "stale.txt")).ShouldBeFalse();
        Directory.Exists(staging.FullName).ShouldBeFalse();
    }

    [Fact]
    public void Remove_OverADirectoryThatIsNotThere_DoesNothingRatherThanThrowing()
    {
        var absent = new DirectoryInfo(Path.Combine(_root.FullName, "never-existed"));

        Should.NotThrow(() => _writer.Remove(absent));
    }

    [Fact]
    public void Remove_OverADirectoryThatIsThere_DeletesItAndWhatIsInside()
    {
        var staging = _writer.CreateStaging(Root);

        _writer.WriteStaged(staging, "rules/acme.dll", [1]);
        _writer.Remove(staging);

        Directory.Exists(staging.FullName).ShouldBeFalse();
    }

    [Fact]
    public void EveryEntryPoint_WithoutItsArgument_Throws()
    {
        Should.Throw<ArgumentNullException>(() => _writer.CreateStaging(null!));
        Should.Throw<ArgumentNullException>(() => _writer.WriteStaged(null!, "a", []));
        Should.Throw<ArgumentNullException>(() => _writer.Commit(null!, _root));
        Should.Throw<ArgumentNullException>(() => _writer.Remove(null!));
    }

    private static PackageVersion Version(string text)
    {
        PackageVersion.TryParse(text, out var version).ShouldBeTrue();

        return version!;
    }
}
