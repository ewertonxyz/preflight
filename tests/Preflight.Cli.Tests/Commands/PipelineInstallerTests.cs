namespace Preflight.Cli.Tests.Commands;

using Preflight.Cli.Commands;

/// <summary>
/// Fixes what <c>preflight pipeline install</c> accepts, what it refuses, and
/// what it deletes.
/// </summary>
/// <remarks>
/// Against real archives and a real install root of its own. The two assertions
/// worth reading twice are the one that proves the pin did <em>not</em> move,
/// and the retention boundary — deleting is the only thing here that cannot be
/// undone. See ADR-033.
/// </remarks>
public sealed class PipelineInstallerTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("preflight-install-");
    private readonly DirectoryInfo _packages = Directory.CreateTempSubdirectory("preflight-packages-");
    private readonly DirectoryInfo _workspace = Directory.CreateTempSubdirectory("preflight-ws-");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public void Dispose()
    {
        TryDelete(_root);
        TryDelete(_packages);
        TryDelete(_workspace);
    }

    [Fact]
    public async Task Install_ForAWellFormedPackage_WritesTheVersionDirectory()
    {
        var package = PackageFixtures.Write(_packages, "projecta", "1.4.0");

        (await Install(package)).ShouldBe(0);

        Reader().Versions("projecta").Select(version => version.ToString()).ShouldBe(["1.4.0"]);
    }

    /// <remarks>
    /// The interesting assertion is what did not happen. If install pinned, one
    /// delivery through a studio's toolchain would move every machine's pin
    /// together and the retained versions would stop being a rollback.
    /// </remarks>
    [Fact]
    public async Task Install_NeverWritesAPin()
    {
        await Install(PackageFixtures.Write(_packages, "projecta", "1.4.0"));

        new MachineStateStore().Read(Root().MachineStatePath).Pins.ShouldBeEmpty();
    }

    [Fact]
    public async Task Install_OfTheSameVersionTwice_Succeeds()
    {
        var package = PackageFixtures.Write(_packages, "projecta", "1.4.0");

        (await Install(package)).ShouldBe(0);
        (await Install(package)).ShouldBe(0);
    }

    [Theory]
    [InlineData(PackageDamage.DigestMismatch)]
    [InlineData(PackageDamage.MissingFromArchive)]
    [InlineData(PackageDamage.UnlistedFile)]
    public async Task Install_ForEveryChecksumProblem_RefusesAndInstallsNothing(PackageDamage damage)
    {
        var package = PackageFixtures.Write(_packages, "projecta", "1.4.0", corrupt: damage);

        await Should.ThrowAsync<PackageManifestException>(() => Install(package));

        Reader().Versions("projecta").ShouldBeEmpty();
    }

    [Fact]
    public async Task Install_WithAnUnknownSchemaVersion_Refuses()
    {
        var package = PackageFixtures.Write(
            _packages, "projecta", "1.4.0", corrupt: PackageDamage.UnknownSchema);

        await Should.ThrowAsync<PackageManifestException>(() => Install(package));
    }

    /// <remarks>
    /// Refused here rather than at the next run, so one person sees it instead
    /// of every machine that pulls the delivery.
    /// </remarks>
    [Fact]
    public async Task Install_WithAnIncompatibleContractRange_RefusesAtInstall()
    {
        var package = PackageFixtures.Write(
            _packages, "projecta", "1.4.0", contractMinimum: "99.0.0");

        var error = await Should.ThrowAsync<PackageManifestException>(() => Install(package));

        error.Message.ShouldContain("99.0.0");
        error.Message.ShouldContain("Preflight.Abstractions");
    }

    [Fact]
    public async Task Install_OfSomethingThatIsNotAPackage_Refuses()
    {
        var path = Path.Combine(_packages.FullName, "not-a-package.zip");

        await File.WriteAllTextAsync(path, "definitely not a zip", TestContext.Current.CancellationToken);

        await Should.ThrowAsync<PackageArchiveException>(() => Install(path));
    }

    [Fact]
    public async Task Install_SweepsPastTheRetentionBoundary()
    {
        foreach (var minor in Enumerable.Range(0, 4))
        {
            await Install(
                PackageFixtures.Write(_packages, "projecta", $"1.{minor}.0"),
                keep: 2);
        }

        Reader().Versions("projecta").Select(version => version.ToString())
            .ShouldBe(["1.2.0", "1.3.0"]);
    }

    [Fact]
    public async Task Install_WithNoGc_DeletesNothing()
    {
        foreach (var minor in Enumerable.Range(0, 4))
        {
            await Install(
                PackageFixtures.Write(_packages, "projecta", $"1.{minor}.0"),
                keep: 2,
                noGc: true);
        }

        Reader().Versions("projecta").Count.ShouldBe(4);
    }

    [Fact]
    public async Task Install_PrintsWhatItDeleted()
    {
        foreach (var minor in Enumerable.Range(0, 3))
        {
            await Install(PackageFixtures.Write(_packages, "projecta", $"1.{minor}.0"), keep: 1);
        }

        _output.ToString().ShouldContain("Removed projecta@1.0.0");
    }

    /// <remarks>
    /// Counted per pipeline and never across the root. A game publishing ten
    /// times a week would otherwise evict the version of the game beside it.
    /// </remarks>
    [Fact]
    public async Task Install_CountsRetentionPerPipeline()
    {
        await Install(PackageFixtures.Write(_packages, "projecta", "1.0.0"), keep: 1);
        await Install(PackageFixtures.Write(_packages, "projectb", "2.0.0"), keep: 1);
        await Install(PackageFixtures.Write(_packages, "projectb", "2.1.0"), keep: 1);

        Reader().Versions("projecta").Count.ShouldBe(1);
        Reader().Versions("projectb").Select(version => version.ToString()).ShouldBe(["2.1.0"]);
    }

    [Theory]
    [InlineData(10, 9, 0)]
    [InlineData(10, 10, 0)]
    [InlineData(10, 11, 1)]
    [InlineData(1, 2, 1)]
    [InlineData(0, 2, 2)]
    public void Collectable_AtTheRetentionBoundary_RemovesTheRightCount(
        int keep, int installed, int expected) =>
        PipelineInstaller.Collectable(Versions(installed), [], keep).Count.ShouldBe(expected);

    /// <remarks>
    /// Whatever its age. The pin is the rollback, and a sweep that could take it
    /// would be a sweep that removes the reason the old versions are kept.
    /// </remarks>
    [Fact]
    public void Collectable_NeverRemovesAReferencedVersion()
    {
        var installed = Versions(5);

        PipelineInstaller.Collectable(installed, [installed[0]], keep: 1)
            .ShouldNotContain(installed[0]);
    }

    private static IReadOnlyList<PackageVersion> Versions(int count) =>
        [.. Enumerable.Range(0, count).Select(minor =>
        {
            PackageVersion.TryParse($"1.{minor}.0", out var version).ShouldBeTrue();

            return version!;
        })];

    private PipelineInstallRoot Root() => new(_root);

    private InstalledPipelineReader Reader() => new(Root());

    private Task<int> Install(string package, int? keep = null, bool noGc = false)
    {
        var environment = CommandEnvironments.For(
            _workspace,
            _output,
            _error,
            TimeProvider.System,
            installRoot: Root(),
            machineState: new MachineStateStore().Read(Root().MachineStatePath));

        return PipelineInstaller.InstallAsync(
            environment, package, keep, noGc, TestContext.Current.CancellationToken);
    }

    private static void TryDelete(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A handle somebody else still holds is not this test's failure, and
            // attributing it to whichever class ran last is worse than leaving a
            // temporary directory behind.
        }
    }
}
