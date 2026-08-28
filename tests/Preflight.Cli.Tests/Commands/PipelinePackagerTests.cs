namespace Preflight.Cli.Tests.Commands;

using System.Text;
using Preflight.Cli.Commands;

/// <summary>
/// Fixes what <c>preflight pipeline pack</c> produces, and what it refuses to
/// pack at all.
/// </summary>
/// <remarks>
/// Determinism is the promise this file exists for, and it is asserted over
/// real bytes rather than over entry order. A checksum published beside a
/// package is worth nothing unless the same tree produces the same archive on
/// somebody else's machine, and the ways that quietly stops being true —
/// enumeration order, file timestamps, the unix mode .NET writes into a zip's
/// external attributes — are all invisible in a diff. See ADR-033.
/// </remarks>
public sealed class PipelinePackagerTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("preflight-pack-");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

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
            // Tolerated, as the plugin fixture utility already tolerates it. A
            // packager test that left an archive handle open would otherwise
            // fail teardown and attribute the defect to this class rather than
            // to the one that opened it.
        }
    }

    private CommandEnvironment Environment(DirectoryInfo workspace) =>
        CommandEnvironments.For(workspace, _output, _error, TimeProvider.System);

    private DirectoryInfo Tree(string name, IReadOnlyList<string> files, string? version = null)
    {
        var directory = _root.CreateSubdirectory(name);

        Write(directory, PackageManifest.FileName, $$"""
            {
              "schemaVersion": 1,
              "name": "{{name}}",
              "version": "{{version ?? "1.4.0"}}",
              "policyFile": "preflight.{{name}}.json",
              "ruleAssemblies": [],
              "abstractionsMinimumVersion": "1.0.0"
            }
            """);

        foreach (var file in files)
        {
            Write(directory, file, $"content of {file}");
        }

        return directory;
    }

    private static void Write(DirectoryInfo directory, string relativePath, string content)
    {
        var path = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
    }

    private Task<int> Pack(DirectoryInfo tree, string output) =>
        PipelinePackager.PackAsync(
            Environment(tree), tree.FullName, output, TestContext.Current.CancellationToken);

    private string Output(string name) => Path.Combine(_root.FullName, name);

    /// <remarks>
    /// Twenty iterations in one process, because a single repeat would pass
    /// against a packager that happened to settle on a stable order once. This
    /// is the cheap half of the promise; the expensive half is the test below.
    /// </remarks>
    [Fact]
    public async Task Pack_RepeatedOverTheSameTree_ProducesIdenticalBytes()
    {
        var tree = Tree("projecta", ["preflight.projecta.json", "rules/acme.dll"]);
        var first = Output("first.zip");

        (await Pack(tree, first)).ShouldBe(0);

        var expected = await File.ReadAllBytesAsync(first, TestContext.Current.CancellationToken);

        for (var iteration = 0; iteration < 20; iteration++)
        {
            var path = Output($"repeat-{iteration}.zip");

            (await Pack(tree, path)).ShouldBe(0);

            (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken))
                .ShouldBe(expected);
        }
    }

    /// <remarks>
    /// The defect repetition inside one process cannot catch. Two trees holding
    /// the same content, whose files were created in opposite orders, are what
    /// two checkouts of one repository look like on two machines — and a
    /// packager that trusted the file system's enumeration order would pass the
    /// test above and fail this one.
    /// </remarks>
    [Fact]
    public async Task Pack_OverTwoTreesWrittenInDifferentOrder_ProducesIdenticalBytes()
    {
        string[] forwards = ["preflight.projecta.json", "rules/a.dll", "rules/b.dll", "rules/c.dll"];

        var one = Tree("projecta", forwards);
        var other = Tree("projecta-reversed", [.. forwards.Reverse()]);

        // The second tree is the same package written backwards, so it carries
        // the same manifest as the first: what differs is only the order the
        // file system was asked to create the files in.
        File.Copy(
            Path.Combine(one.FullName, PackageManifest.FileName),
            Path.Combine(other.FullName, PackageManifest.FileName),
            overwrite: true);

        (await Pack(one, Output("one.zip"))).ShouldBe(0);
        (await Pack(other, Output("other.zip"))).ShouldBe(0);

        (await File.ReadAllBytesAsync(Output("other.zip"), TestContext.Current.CancellationToken))
            .ShouldBe(await File.ReadAllBytesAsync(
                Output("one.zip"), TestContext.Current.CancellationToken));
    }

    /// <remarks>
    /// Any depth, ignoring case, exactly as <c>ReservedFileNames</c> already
    /// treats it. <c>Preflight.Workspace.JSON</c> in a subfolder is the same
    /// leak: the manifest describes a checkout, its <c>compileProbe.inputs</c>
    /// change whenever <c>src/</c> does, and shipped inside a package it would
    /// age silently while serving <c>(cached)</c> over a different tree.
    /// </remarks>
    [Theory]
    [InlineData("preflight.workspace.json")]
    [InlineData("nested/preflight.workspace.json")]
    [InlineData("Preflight.Workspace.JSON")]
    public async Task Pack_WhenTheTreeContainsTheWorkspaceManifest_Refuses(string relativePath)
    {
        var tree = Tree("projecta", ["preflight.projecta.json", relativePath]);

        var exception = await Should.ThrowAsync<PipelinePackException>(
            () => Pack(tree, Output("out.zip")));

        exception.Message.ShouldContain("workspace", Case.Insensitive);
        File.Exists(Output("out.zip")).ShouldBeFalse();
    }

    /// <remarks>
    /// No <c>--force</c>, on purpose. Overwriting would make the output an
    /// input on the second run and break determinism underneath the test that
    /// asserts it.
    /// </remarks>
    [Fact]
    public async Task Pack_WhenTheOutputFileExists_Refuses()
    {
        var tree = Tree("projecta", ["preflight.projecta.json"]);
        var output = Output("taken.zip");

        await File.WriteAllTextAsync(output, "not a package", TestContext.Current.CancellationToken);

        (await Should.ThrowAsync<PipelinePackException>(() => Pack(tree, output)))
            .Message.ShouldContain(output);

        (await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken))
            .ShouldBe("not a package");
    }

    [Fact]
    public async Task Pack_WithTheOutputInsideTheTree_Refuses()
    {
        var tree = Tree("projecta", ["preflight.projecta.json"]);
        var inside = Path.Combine(tree.FullName, "out.zip");

        await Should.ThrowAsync<PipelinePackException>(() => Pack(tree, inside));

        File.Exists(inside).ShouldBeFalse();
    }

    /// <remarks>
    /// A package carrying a policy and no rules is the common case, not the
    /// degenerate one: most productions tighten limits without writing a line
    /// of C#. A tree holding nothing but its own manifest is the refusal.
    /// </remarks>
    [Fact]
    public async Task Pack_OverAPolicyOnlyTree_Succeeds()
    {
        var tree = Tree("projectb", ["preflight.projectb.json"]);

        (await Pack(tree, Output("policy-only.zip"))).ShouldBe(0);

        File.Exists(Output("policy-only.zip")).ShouldBeTrue();
    }

    [Fact]
    public async Task Pack_OverATreeWithNothingButItsManifest_Refuses()
    {
        var tree = Tree("projecta", []);

        await Should.ThrowAsync<PipelinePackException>(() => Pack(tree, Output("empty.zip")));
    }

    /// <summary>
    /// Two paths differing only in case are a collision, whatever the disk says.
    /// </summary>
    /// <remarks>
    /// Asserted against the pure check rather than through a real tree, and not
    /// by preference: NTFS will not hold two names differing only in case, so
    /// the condition is unreachable from a test that builds a directory here. It
    /// is entirely reachable on the file systems that will — and there the
    /// second file silently replaces the first on installation, leaving a package
    /// one assembly short with every digest in its manifest still matching.
    /// </remarks>
    /// <summary>
    /// Two paths differing only in case are a collision, whatever the disk says.
    /// </summary>
    /// <remarks>
    /// Asserted against the pure check rather than through a real tree, and not
    /// by preference: NTFS refuses two names differing only in case, and refuses
    /// a junction that would produce them, so the condition is unreachable from
    /// a test that builds a directory here. It is entirely reachable on the file
    /// systems that allow it — and there the second file silently replaces the
    /// first on installation, leaving a package one assembly short with every
    /// digest in its manifest still matching.
    /// </remarks>
    [Theory]
    [InlineData("rules/acme.dll|rules/Acme.dll", "rules/Acme.dll")]
    [InlineData("Preflight.projecta.json|preflight.projecta.json", "preflight.projecta.json")]
    [InlineData("a.json|b.json|A.JSON", "A.JSON")]
    public void RequireDistinctIgnoringCase_ForPathsThatDifferOnlyInCase_RefusesNamingTheSecond(
        string paths, string expected) =>
        Should.Throw<PipelinePackException>(
            () => PipelinePackager.RequireDistinctIgnoringCase("the tree", paths.Split('|')))
            .Message.ShouldContain(expected);

    [Theory]
    [InlineData("")]
    [InlineData("a.json")]
    [InlineData("rules/acme.dll|rules/other.dll|preflight.projecta.json")]
    public void RequireDistinctIgnoringCase_ForPathsThatAreAllDistinct_Accepts(string paths) =>
        Should.NotThrow(() => PipelinePackager.RequireDistinctIgnoringCase(
            "the tree", paths.Split('|', StringSplitOptions.RemoveEmptyEntries)));

    [Fact]
    public void RequireDistinctIgnoringCase_WithoutItsArguments_Throws()
    {
        Should.Throw<ArgumentNullException>(
            () => PipelinePackager.RequireDistinctIgnoringCase(null!, []));
        Should.Throw<ArgumentNullException>(
            () => PipelinePackager.RequireDistinctIgnoringCase("the tree", null!));
    }

    [Fact]
    public async Task Pack_OverADirectoryThatIsNotThere_RefusesNamingIt()
    {
        var absent = Path.Combine(_root.FullName, "nowhere");

        var exception = await Should.ThrowAsync<PipelinePackException>(
            () => PipelinePackager.PackAsync(
                Environment(_root), absent, Output("out.zip"), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(absent);
    }

    [Fact]
    public async Task Pack_WithoutAManifest_RefusesNamingIt()
    {
        var tree = _root.CreateSubdirectory("no-manifest");

        Write(tree, "preflight.projecta.json", """{ "schemaVersion": 1 }""");

        (await Should.ThrowAsync<PackageManifestException>(() => Pack(tree, Output("none.zip"))))
            .Message.ShouldContain(PackageManifest.FileName);
    }

    [Fact]
    public async Task Pack_WhenTheManifestNamesAPolicyFileTheTreeLacks_Refuses()
    {
        var tree = _root.CreateSubdirectory("no-policy");

        Write(tree, PackageManifest.FileName, """
            {
              "schemaVersion": 1,
              "name": "projecta",
              "version": "1.4.0",
              "policyFile": "preflight.projecta.json",
              "ruleAssemblies": [],
              "abstractionsMinimumVersion": "1.0.0"
            }
            """);
        Write(tree, "rules/acme.dll", "not the policy");

        (await Should.ThrowAsync<PipelinePackException>(() => Pack(tree, Output("no-policy.zip"))))
            .Message.ShouldContain("preflight.projecta.json");
    }

    /// <remarks>
    /// The two ends of the phase meeting. <c>pack</c> writes the digest map and
    /// <c>install</c> is the only thing that reads it, so a disagreement between
    /// them is invisible to either one's own tests — and it is the whole
    /// contract between a studio's toolchain and the machines it delivers to.
    /// </remarks>
    [Fact]
    public async Task Pack_ProducesAPackageTheInstallerAccepts()
    {
        var tree = Tree("projecta", ["preflight.projecta.json", "rules/acme.dll"]);
        var package = Output("installable.zip");

        (await Pack(tree, package)).ShouldBe(0);

        var installRoot = new PipelineInstallRoot(_root.CreateSubdirectory("install-root"));
        var workspace = _root.CreateSubdirectory("checkout");

        var environment = CommandEnvironments.For(
            workspace, _output, _error, TimeProvider.System, installRoot: installRoot);

        (await PipelineInstaller.InstallAsync(
            environment, package, keep: null, noGc: false, TestContext.Current.CancellationToken))
            .ShouldBe(0);

        PackageVersion.TryParse("1.4.0", out var version).ShouldBeTrue();
        environment.InstalledPipelines.Versions("projecta").ShouldBe([version!]);
    }
}
