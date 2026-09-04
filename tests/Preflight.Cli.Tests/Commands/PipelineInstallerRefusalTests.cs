namespace Preflight.Cli.Tests.Commands;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using NSubstitute;
using Preflight.Cli.Commands;
using Preflight.Cli.Pipelines;
using Preflight.Cli.Services;
using Preflight.Cli.Storage;
using Preflight.TestSupport;

/// <summary>
/// The refusals an installer has to make before it writes a byte, and the one
/// thing it has to do after a write fails.
/// </summary>
/// <remarks>
/// <para>
/// Every case here is a package that must not end up on disk. The reason they
/// are worth their own file is that each one produces a package which is
/// <em>almost</em> valid: an archive whose manifest is missing, a contract
/// version that is not a version, two entries a case-insensitive disk cannot
/// hold apart, an entry whose name climbs out of the version directory. All of
/// them install cleanly under an implementation that checks nothing, and the run
/// that follows reports success having loaded rules nobody published.
/// </para>
/// <para>
/// The archives are written by hand rather than through <c>PackageFixtures</c>,
/// because what is being built is precisely what a well-behaved packer will not
/// produce.
/// </para>
/// </remarks>
public sealed class PipelineInstallerRefusalTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("preflight-install-refusal-");
    private readonly DirectoryInfo _installRoot;
    private readonly DirectoryInfo _workspace;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public PipelineInstallerRefusalTests()
    {
        _installRoot = _root.CreateSubdirectory("install-root");
        _workspace = _root.CreateSubdirectory("checkout");
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();

        try
        {
            _root.Delete(recursive: true);
        }
        catch (IOException)
        {
            // Tolerated, as the plugin fixture utility already tolerates it.
        }
    }

    private CommandEnvironment Environment(
        IInstallRootWriter? writer = null, MachineState? state = null) =>
        CommandEnvironments.For(
            _workspace,
            _output,
            _error,
            TimeProvider.System,
            installRoot: new PipelineInstallRoot(_installRoot),
            machineState: state,
            installWriter: writer);

    private Task<int> Install(string package, IInstallRootWriter? writer = null, MachineState? state = null) =>
        PipelineInstaller.InstallAsync(
            Environment(writer, state), package, keep: null, noGc: false,
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Writes an archive holding the entries given, with a manifest whose
    /// digests already match them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Straight to <see cref="ZipArchive"/>, so that entry names a well-behaved
    /// packer would refuse to produce can still be produced here — which is the
    /// whole point, because an installer only ever has to survive archives
    /// somebody else wrote.
    /// </para>
    /// <para>
    /// The digests are computed rather than left empty, and that is not
    /// convenience. With an empty map every one of these packages fails on "not
    /// in its manifest" before reaching the check its test is about, so every
    /// case would pass for the wrong reason.
    /// </para>
    /// </remarks>
    private string Archive(
        string name, string contractMinimum, params (string Entry, string Content)[] entries)
    {
        var path = Path.Combine(_root.FullName, name);
        var digests = entries.ToDictionary(
            entry => entry.Entry,
            entry => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Content))),
            StringComparer.Ordinal);

        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            Add(archive, PackageManifest.FileName, Manifest(contractMinimum, digests));

            foreach (var (entry, content) in entries)
            {
                Add(archive, entry, content);
            }
        }

        return path;
    }

    /// <remarks>
    /// Bytes, not a <see cref="StreamWriter"/>. <c>Encoding.UTF8</c> carries a
    /// preamble and the writer emits it, so the entry would not hash to what
    /// <see cref="Encoding.GetBytes(string)"/> produced for the digest map — and
    /// every package here would be refused as damaged, one check earlier than
    /// the one being tested.
    /// </remarks>
    private static void Add(ZipArchive archive, string relativePath, string content)
    {
        using var entry = archive.CreateEntry(relativePath).Open();

        entry.Write(Encoding.UTF8.GetBytes(content));
    }

    private string Archive(string name, params (string Entry, string Content)[] entries) =>
        Archive(name, ContractVersion.Current, entries);

    private static string Manifest(
        string? contractMinimum = null,
        IReadOnlyDictionary<string, string>? digests = null) =>
        $$"""
        {
          "schemaVersion": 1,
          "name": "projecta",
          "version": "1.4.0",
          "policyFile": "preflight.projecta.json",
          "ruleAssemblies": [],
          "abstractionsMinimumVersion": "{{contractMinimum ?? ContractVersion.Current}}",
          "sha256ByRelativePath": {
            {{string.Join(",\n    ", (digests ?? new Dictionary<string, string>())
                .Select(pair => $"\"{pair.Key}\": \"{pair.Value}\""))}}
          }
        }
        """;

    [Fact]
    public async Task Install_OverAnArchiveWithNoManifest_RefusesNamingWhatIsMissing()
    {
        var path = Path.Combine(_root.FullName, "no-manifest.zip");

        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(
                archive.CreateEntry("preflight.projecta.json").Open(), Encoding.UTF8);

            writer.Write("{}");
        }

        var package = path;

        (await Should.ThrowAsync<PackageManifestException>(() => Install(package)))
            .Message.ShouldContain(PackageManifest.FileName);

        _installRoot.EnumerateDirectories().ShouldBeEmpty();
    }

    /// <remarks>
    /// Not an incompatible contract version but an unparseable one, and the two
    /// deserve different sentences: "is not a contract version" sends the author
    /// to their manifest, "this build provides" sends them to the tool.
    /// </remarks>
    [Fact]
    public async Task Install_WithAContractVersionThatIsNotAVersion_RefusesBeforeComparingIt()
    {
        var package = Archive("bad-contract.zip", "latest", ("preflight.projecta.json", "{}"));

        (await Should.ThrowAsync<PackageManifestException>(() => Install(package)))
            .Message.ShouldContain("latest");

        _installRoot.EnumerateDirectories().ShouldBeEmpty();
    }

    /// <remarks>
    /// A zip can hold both; a disk that ignores case cannot. Installing would
    /// put one assembly where two were published, with every digest in the
    /// manifest still matching — which is the shape of defect that survives
    /// every other check this installer makes.
    /// </remarks>
    [Fact]
    public async Task Install_WithTwoEntriesDifferingOnlyInCase_Refuses()
    {
        var package = Archive("case.zip", ("rules/acme.dll", "one"), ("rules/Acme.dll", "two"));

        (await Should.ThrowAsync<PackageArchiveException>(() => Install(package)))
            .Message.ShouldContain("case");

        _installRoot.EnumerateDirectories().ShouldBeEmpty();
    }

    /// <remarks>
    /// Zip slip, in the three spellings a hand-written archive can carry. Each
    /// is refused by name, and the assertion that matters is the second one:
    /// nothing outside the install root was created.
    /// </remarks>
    [Theory]
    [InlineData("../escaped.dll")]
    [InlineData("rules/../../escaped.dll")]
    public async Task Install_WithAnEntryThatEscapesTheVersionDirectory_RefusesAndWritesNothingOutside(
        string entry)
    {
        var package = Archive("slip.zip", (entry, "payload"));

        (await Should.ThrowAsync<PackageArchiveException>(() => Install(package)))
            .Message.ShouldContain(entry);

        File.Exists(Path.Combine(_root.FullName, "escaped.dll")).ShouldBeFalse();
        _installRoot.EnumerateDirectories().ShouldBeEmpty();
    }

    /// <remarks>
    /// A rooted entry, in the spelling a zip actually carries: the format uses
    /// forward slashes, and a leading one is rooted to
    /// <see cref="Path.IsPathRooted(string)"/> on Windows as well. Written with
    /// a drive letter it would be caught by the colon instead, which is a
    /// different arm of the same refusal.
    /// </remarks>
    [Fact]
    public async Task Install_WithARootedEntry_Refuses()
    {
        var package = Archive("rooted.zip", ("/rooted.dll", "payload"));

        (await Should.ThrowAsync<PackageArchiveException>(() => Install(package)))
            .Message.ShouldContain("rooted.dll");

        _installRoot.EnumerateDirectories().ShouldBeEmpty();
    }

    /// <remarks>
    /// The third arm: a Windows drive letter, caught by the colon rather than by
    /// <see cref="Path.IsPathRooted(string)"/>, because a zip entry named
    /// <c>C:/evil.dll</c> is not a rooted path to every API that looks at it.
    /// </remarks>
    [Fact]
    public async Task Install_WithAnEntryNamingADrive_Refuses()
    {
        var package = Archive("drive.zip", ("C:/evil.dll", "payload"));

        await Should.ThrowAsync<PackageArchiveException>(() => Install(package));

        _installRoot.EnumerateDirectories().ShouldBeEmpty();
    }

    /// <summary>
    /// A write that fails halfway leaves no staging tree behind.
    /// </summary>
    /// <remarks>
    /// The one case that is about what happens <em>after</em> a byte is written.
    /// A staging directory abandoned inside the install root is the sort of
    /// thing somebody finds a year later and cannot attribute — and worse, an
    /// interrupted install that left a half-extracted version where the resolver
    /// can see it is a run with a partial rule set reporting success.
    /// </remarks>
    [Fact]
    public async Task Install_WhenTheWriteFails_RemovesTheStagingTreeAndPropagates()
    {
        var writer = Substitute.For<IInstallRootWriter>();
        var staging = _installRoot.CreateSubdirectory("staging");

        writer.CreateStaging(Arg.Any<PipelineInstallRoot>()).Returns(staging);
        writer
            .When(w => w.Commit(Arg.Any<DirectoryInfo>(), Arg.Any<DirectoryInfo>()))
            .Do(_ => throw new IOException("the volume went away"));

        var package = Archive("interrupted.zip", ("preflight.projecta.json", "{}"));

        await Should.ThrowAsync<IOException>(() => Install(package, writer));

        writer.Received(1).Remove(staging);
    }

    /// <summary>
    /// The retention sweep keeps the pinned version whatever its age.
    /// </summary>
    /// <remarks>
    /// <c>Collectable</c> is tested alone and takes the referenced set as a
    /// parameter; what is untested there is the line that builds that set from
    /// the machine's pins. Getting it wrong deletes the version somebody
    /// deliberately pinned, on the next install, and deleting is the one thing
    /// here that cannot be undone.
    /// </remarks>
    [Fact]
    public async Task Install_WithAPinnedVersionOutsideTheWindow_KeepsIt()
    {
        PackageVersion.TryParse("1.0.0", out var pinned).ShouldBeTrue();

        var state = MachineState.Empty with
        {
            Keep = 1,
            Pins = new Dictionary<string, PackageVersion>(StringComparer.OrdinalIgnoreCase)
            {
                ["projecta"] = pinned!,
            },
        };

        Given("1.0.0");
        Given("1.1.0");

        var package = Archive("newest.zip", ("preflight.projecta.json", "{}"));

        (await Install(package, state: state)).ShouldBe(0);

        var root = new PipelineInstallRoot(_installRoot);

        // Keep is 1 and three versions are installed, so the sweep had a
        // decision to make. The pinned one survives it; the unreferenced middle
        // one does not.
        root.VersionDirectory("projecta", pinned!).Exists.ShouldBeTrue();
        _output.ToString().ShouldContain("Removed projecta@1.1.0");
    }

    /// <summary>Puts a version on disk without going through install.</summary>
    private void Given(string version)
    {
        var directory = new PipelineInstallRoot(_installRoot)
            .VersionDirectory("projecta", Parse(version));

        Directory.CreateDirectory(directory.FullName);

        File.WriteAllText(
            Path.Combine(directory.FullName, PackageManifest.FileName),
            Manifest().Replace("\"version\": \"1.4.0\"", $"\"version\": \"{version}\"", StringComparison.Ordinal),
            Encoding.UTF8);
    }

    private static PackageVersion Parse(string text)
    {
        PackageVersion.TryParse(text, out var version).ShouldBeTrue();

        return version!;
    }
}
