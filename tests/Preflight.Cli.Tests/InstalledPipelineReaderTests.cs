namespace Preflight.Cli.Tests;

using System.Text.Json;

/// <summary>
/// Fixes what the install root is read as.
/// </summary>
/// <remarks>
/// A stray directory there is skipped rather than raised. The root is a place a
/// person can open in a file manager, and a folder somebody left behind must not
/// stop a run that was never going to read it — the same judgement the pipeline
/// selector makes about a stray <c>preflight.*.json</c>.
/// </remarks>
public sealed class InstalledPipelineReaderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("preflight-reader-");

    public void Dispose() => _root.Delete(recursive: true);

    private PipelineInstallRoot Root => new(_root);

    private InstalledPipelineReader Reader => new(Root);

    [Fact]
    public void Versions_WithNothingInstalled_IsEmpty() => Reader.Versions("projecta").ShouldBeEmpty();

    /// <summary>
    /// Every shape of manifest this build will not read, refused by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The literal <c>null</c> is the one worth naming: it is valid JSON that
    /// deserialises to nothing, so it is the only malformed shape that does not
    /// raise on the way in. Read as an empty manifest it would install a package
    /// with no name, no version and no digests — which is to say, unverified.
    /// </para>
    /// <para>
    /// The empty-string cases are the other half of a null check the reader makes
    /// with a pattern that accepts both: a member absent and a member present and
    /// blank have to reach the same refusal, or a manifest with
    /// <c>"policyFile": ""</c> installs and resolves to the version directory
    /// itself.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("null", "empty")]
    [InlineData("""{ "schemaVersion": 1, "name": "", "version": "1.4.0" }""", "pipeline")]
    [InlineData("""{ "schemaVersion": 1, "version": "1.4.0" }""", "pipeline")]
    [InlineData(
        """{ "schemaVersion": 1, "name": "projecta", "version": "1.4.0", "policyFile": "" }""",
        "policy file")]
    [InlineData(
        """{ "schemaVersion": 1, "name": "projecta", "version": "1.4.0" }""",
        "policy file")]
    [InlineData(
        """
        { "schemaVersion": 1, "name": "projecta", "version": "1.4.0",
          "policyFile": "p.json", "abstractionsMinimumVersion": "" }
        """,
        "contract version")]
    [InlineData(
        """{ "schemaVersion": 1, "name": "projecta", "version": "1.4.0", "policyFile": "p.json" }""",
        "contract version")]
    public void Read_ForEveryUnusableManifest_RefusesNamingWhatIsWrong(string json, string expected)
    {
        var path = Path.Combine(_root.FullName, "pipeline.json");

        File.WriteAllText(path, json);

        Should.Throw<PackageManifestException>(() => InstalledPipelineReader.Read(path))
            .Message.ShouldContain(expected);
    }

    /// <remarks>
    /// The two collection members are optional, and a manifest that omits them
    /// has to read as empty rather than as null. A policy-only package omits
    /// <c>ruleAssemblies</c>, which is the common case, and a null there would
    /// throw on the first enumeration — during an install, after the digests
    /// were already checked.
    /// </remarks>
    [Fact]
    public void Read_ForAManifestOmittingItsOptionalCollections_ReadsThemAsEmpty()
    {
        var path = Path.Combine(_root.FullName, "pipeline.json");

        File.WriteAllText(path, """
            {
              "schemaVersion": 1,
              "name": "projecta",
              "version": "1.4.0",
              "policyFile": "preflight.projecta.json",
              "abstractionsMinimumVersion": "1.0.0"
            }
            """);

        var manifest = InstalledPipelineReader.Read(path);

        manifest.RuleAssemblies.ShouldBeEmpty();
        manifest.Sha256ByRelativePath.ShouldBeEmpty();
        manifest.AbstractionsMaximumVersion.ShouldBeNull();
    }

    [Fact]
    public void Pipelines_WithNothingInstalled_IsEmpty() => Reader.Pipelines().ShouldBeEmpty();

    [Fact]
    public void Versions_AreOrderedNumerically()
    {
        Install("projecta", "1.9.0");
        Install("projecta", "1.10.0");
        Install("projecta", "1.2.0");

        Reader.Versions("projecta").Select(version => version.ToString())
            .ShouldBe(["1.2.0", "1.9.0", "1.10.0"]);
    }

    [Fact]
    public void Versions_SkipsADirectoryThatIsNotAVersion()
    {
        Install("projecta", "1.4.0");
        Directory.CreateDirectory(Path.Combine(Root.PipelineDirectory("projecta").FullName, "notes"));

        Reader.Versions("projecta").Count.ShouldBe(1);
    }

    /// <remarks>
    /// A version directory without a manifest is a half-installed tree or
    /// something somebody unzipped by hand. Either way it is not a package, and
    /// resolving to it would run a smaller set of checks than the pipeline
    /// declares.
    /// </remarks>
    [Fact]
    public void Versions_SkipsAVersionDirectoryWithNoManifest()
    {
        Directory.CreateDirectory(Root.VersionDirectory("projecta", Parse("1.4.0")).FullName);

        Reader.Versions("projecta").ShouldBeEmpty();
    }

    [Fact]
    public void Pipelines_ListsOnlyNamesWithAVersionInstalled()
    {
        Install("projecta", "1.4.0");
        Directory.CreateDirectory(Path.Combine(_root.FullName, "pipelines", "projectb"));

        Reader.Pipelines().ShouldBe(["projecta"]);
    }

    [Fact]
    public void Pipelines_AreOrderedOrdinally()
    {
        Install("projectb", "1.0.0");
        Install("projecta", "1.0.0");

        Reader.Pipelines().ShouldBe(["projecta", "projectb"]);
    }

    [Fact]
    public void Manifest_ReadsTheInstalledPackage()
    {
        Install("projecta", "1.4.0");

        var manifest = Reader.Manifest("projecta", Parse("1.4.0"));

        manifest.Name.ShouldBe("projecta");
        manifest.Version.ToString().ShouldBe("1.4.0");
        manifest.PolicyFile.ShouldBe("preflight.projecta.json");
    }

    [Fact]
    public void Read_ForAnAbsentManifest_Throws() =>
        Should.Throw<PackageManifestException>(
            () => InstalledPipelineReader.Read(Path.Combine(_root.FullName, "nope.json")));

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "schemaVersion": 99, "name": "a", "version": "1.0.0" }""")]
    [InlineData("""{ "schemaVersion": 1, "name": "../evil", "version": "1.0.0" }""")]
    [InlineData("""{ "schemaVersion": 1, "name": "a", "version": "1.0" }""")]
    [InlineData("""{ "schemaVersion": 1, "name": "a", "version": "1.0.0" }""")]
    public void Read_ForEveryUnusableManifest_Throws(string content)
    {
        var path = Path.Combine(_root.FullName, PackageManifest.FileName);

        File.WriteAllText(path, content);

        Should.Throw<PackageManifestException>(() => InstalledPipelineReader.Read(path));
    }

    [Fact]
    public void Read_WithoutAContractVersion_Throws()
    {
        var path = Path.Combine(_root.FullName, PackageManifest.FileName);

        File.WriteAllText(
            path,
            """{ "schemaVersion": 1, "name": "a", "version": "1.0.0", "policyFile": "p.json" }""");

        Should.Throw<PackageManifestException>(() => InstalledPipelineReader.Read(path));
    }

    private static PackageVersion Parse(string text)
    {
        PackageVersion.TryParse(text, out var version).ShouldBeTrue();

        return version!;
    }

    private void Install(string name, string version)
    {
        var directory = Root.VersionDirectory(name, Parse(version));

        directory.Create();

        var manifest = new
        {
            schemaVersion = 1,
            name,
            version,
            policyFile = $"preflight.{name}.json",
            ruleAssemblies = Array.Empty<string>(),
            abstractionsMinimumVersion = "1.0.0",
            sha256ByRelativePath = new Dictionary<string, string>(StringComparer.Ordinal),
        };

        File.WriteAllText(
            Path.Combine(directory.FullName, PackageManifest.FileName),
            JsonSerializer.Serialize(manifest, ManifestSerialization.Options));
    }
}
