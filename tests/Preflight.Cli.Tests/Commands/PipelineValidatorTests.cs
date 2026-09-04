namespace Preflight.Cli.Tests.Commands;

using Preflight.Cli.Commands;
using Preflight.Cli.Pipelines;
using Preflight.Cli.Storage;
using Preflight.TestSupport;

/// <summary>
/// Fixes that <c>preflight pipeline validate</c> reports everything wrong with
/// a source tree in one pass.
/// </summary>
/// <remarks>
/// One edit to fix, not four runs to discover. That is the same promise policy
/// loading already makes — every error across every document, accumulated — and
/// this is it extended over the other half of a package: the manifest, the
/// assemblies it names, and the contract version those assemblies were built
/// against. An author publishing a pipeline is somebody with a build to get
/// out, and a validator that stops at the first problem spends their afternoon
/// one line at a time.
/// </remarks>
public sealed class PipelineValidatorTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("preflight-validate-");
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
            // Tolerated, for the reason the plugin fixture utility gives: a
            // load context holding an assembly open must not make teardown
            // blame this class.
        }
    }


    private Task<int> Validate(DirectoryInfo tree) =>
        PipelineValidator.ValidateAsync(
            CommandEnvironments.For(tree, _output, _error, TimeProvider.System),
            tree.FullName,
            TestContext.Current.CancellationToken);

    /// <remarks>
    /// Six problems, deliberately of six different kinds, in one tree. Any
    /// validator that stopped at the first would report one of them and look
    /// like it worked.
    /// </remarks>
    [Fact]
    public async Task Validate_OverATreeWithSixKindsOfError_ReportsThemAllAtOnce()
    {
        var tree = _root.CreateSubdirectory("six");

        WorkspaceFiles.Write(tree, PackageManifest.FileName, """
            {
              "schemaVersion": 1,
              "name": "projecta",
              "version": "1.4.0",
              "policyFile": "preflight.projecta.json",
              "ruleAssemblies": ["rules/absent.dll", "rules/broken.dll"],
              "abstractionsMinimumVersion": "99.0.0"
            }
            """);

        // 4 and 5: an unknown root key, and a rule id nothing declares.
        WorkspaceFiles.Write(tree, "preflight.projecta.json", """
            {
              "schemaVersion": 1,
              "notAKey": true,
              "rules": { "acme.textures.absent": { "enabled": true } }
            }
            """);

        // 1: a workspace manifest, which pack refuses to ship.
        WorkspaceFiles.Write(tree, "nested/preflight.workspace.json", """{ "tools": [] }""");

        // 3: named, present, and not an assembly. 2 is 'rules/absent.dll',
        // which the manifest names and the tree does not hold.
        WorkspaceFiles.Write(tree, "rules/broken.dll", "this is not a PE file");

        var problems = (await Should.ThrowAsync<PipelineValidationException>(
            () => Validate(tree))).Problems;

        problems.Count.ShouldBeGreaterThanOrEqualTo(6);

        var report = string.Join("\n", problems);

        report.ShouldContain("preflight.workspace.json");
        report.ShouldContain("rules/absent.dll");
        report.ShouldContain("broken.dll");
        report.ShouldContain("notAKey");
        report.ShouldContain("acme.textures.absent");
        report.ShouldContain("99.0.0");
    }

    [Fact]
    public async Task Validate_OverACleanTree_ReportsNothing()
    {
        var tree = _root.CreateSubdirectory("clean");

        WorkspaceFiles.Write(tree, PackageManifest.FileName, $$"""
            {
              "schemaVersion": 1,
              "name": "projecta",
              "version": "1.4.0",
              "policyFile": "preflight.projecta.json",
              "ruleAssemblies": [],
              "abstractionsMinimumVersion": "{{ContractVersion.Current}}"
            }
            """);

        WorkspaceFiles.Write(tree, "preflight.projecta.json", """
            {
              "schemaVersion": 1,
              "rules": { "core.workspace.toolchain": { "enabled": true } }
            }
            """);

        (await Validate(tree)).ShouldBe(0);

        _output.ToString().ShouldContain("projecta@1.4.0");
    }

    [Fact]
    public async Task Validate_WithoutAManifest_RefusesNamingIt()
    {
        var tree = _root.CreateSubdirectory("no-manifest");

        WorkspaceFiles.Write(tree, "preflight.projecta.json", """{ "schemaVersion": 1 }""");

        (await Should.ThrowAsync<PackageManifestException>(() => Validate(tree)))
            .Message.ShouldContain(PackageManifest.FileName);
    }

    /// <remarks>
    /// The policy document is the one file whose absence stops the rest: every
    /// other check reads it. It is still reported beside whatever else the pass
    /// found, rather than short-circuiting the run.
    /// </remarks>
    [Fact]
    public async Task Validate_WhenThePolicyFileIsAbsent_SaysSoWithoutFallingOver()
    {
        var tree = _root.CreateSubdirectory("no-policy");

        WorkspaceFiles.Write(tree, PackageManifest.FileName, $$"""
            {
              "schemaVersion": 1,
              "name": "projecta",
              "version": "1.4.0",
              "policyFile": "preflight.projecta.json",
              "ruleAssemblies": [],
              "abstractionsMinimumVersion": "{{ContractVersion.Current}}"
            }
            """);

        (await Should.ThrowAsync<PipelineValidationException>(() => Validate(tree)))
            .Message.ShouldContain("preflight.projecta.json");
    }

    /// <remarks>
    /// Not a compatibility failure but a spelling one, and the two produce
    /// different sentences. "Not a contract version" sends the author to the
    /// manifest; "this build provides" sends them to the engine.
    /// </remarks>
    [Fact]
    public async Task Validate_WithAContractVersionThatIsNotAVersion_SaysSoRatherThanComparingIt()
    {
        var tree = _root.CreateSubdirectory("bad-contract");

        WorkspaceFiles.Write(tree, PackageManifest.FileName, """
            {
              "schemaVersion": 1,
              "name": "projecta",
              "version": "1.4.0",
              "policyFile": "preflight.projecta.json",
              "ruleAssemblies": [],
              "abstractionsMinimumVersion": "latest"
            }
            """);

        WorkspaceFiles.Write(tree, "preflight.projecta.json", """{ "schemaVersion": 1 }""");

        (await Should.ThrowAsync<PipelineValidationException>(() => Validate(tree)))
            .Message.ShouldContain("latest");
    }

    /// <remarks>
    /// The loader's own refusal, carried through unreworded. Two vocabularies
    /// for one file would make the same problem read differently depending on
    /// which command found it.
    /// </remarks>
    [Fact]
    public async Task Validate_WithAPolicyThatWillNotParse_ReportsTheLoadersOwnMessage()
    {
        var tree = _root.CreateSubdirectory("bad-policy");

        WorkspaceFiles.Write(tree, PackageManifest.FileName, $$"""
            {
              "schemaVersion": 1,
              "name": "projecta",
              "version": "1.4.0",
              "policyFile": "preflight.projecta.json",
              "ruleAssemblies": [],
              "abstractionsMinimumVersion": "{{ContractVersion.Current}}"
            }
            """);

        WorkspaceFiles.Write(tree, "preflight.projecta.json", "{ this is not json");

        (await Should.ThrowAsync<PipelineValidationException>(() => Validate(tree)))
            .Problems.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Validate_OverADirectoryThatIsNotThere_RefusesNamingIt()
    {
        var absent = Path.Combine(_root.FullName, "nowhere");

        var exception = await Should.ThrowAsync<PipelineValidationException>(
            () => PipelineValidator.ValidateAsync(
                CommandEnvironments.For(_root, _output, _error, TimeProvider.System),
                absent,
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(absent);
    }
}
