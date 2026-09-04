namespace Preflight.Cli.Tests.Commands;

using Preflight.Cli.Commands;
using Preflight.Cli.Pipelines;
using Preflight.Cli.Policy;
using Preflight.Cli.Storage;
using Preflight.TestSupport;

/// <summary>
/// Drives every <c>pipeline</c> subcommand through the real parser and the real
/// dispatch, rather than by calling its handler directly.
/// </summary>
/// <remarks>
/// <para>
/// There is exactly one defect this file exists to catch, and no other test in
/// the suite can see it: the dispatch reads its arguments by <em>name</em> —
/// <c>parse.GetValue&lt;string&gt;("source")</c>, <c>("--output")</c>,
/// <c>("selector")</c>, <c>("--keep")</c> — and those names are strings that
/// have to agree with what the command builders declare, one file away and two
/// hundred lines apart. A name that disagrees does not fail to compile and does
/// not fail a handler test; it throws inside the parser at runtime, on the first
/// real invocation, which is the last place anybody wants to find it.
/// </para>
/// <para>
/// The unit tests around each handler are stronger than these on behaviour and
/// blind to this: they hand the handler its arguments as C# values, so the
/// string that the parser would have looked up never takes part. Here the whole
/// path runs — parse, package resolution, plugin composition, the switch arm —
/// so a wrong name, a missing arm, or an argument arity that quietly turns a
/// value into <see langword="null"/> shows up as a failure with the command
/// named.
/// </para>
/// <para>
/// The assertions are deliberately thin. What each command <em>does</em> is
/// fixed by its own tests; what is asserted here is that the invocation arrives
/// at the right handler carrying the right values, which is why every case
/// asserts an exit code and one piece of evidence that the argument survived the
/// trip.
/// </para>
/// </remarks>
public sealed class CommandDispatchTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("preflight-dispatch-");
    private readonly DirectoryInfo _workspace;
    private readonly DirectoryInfo _installRoot;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public CommandDispatchTests()
    {
        _workspace = _root.CreateSubdirectory("checkout");
        _installRoot = _root.CreateSubdirectory("install-root");
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
            // Tolerated, as the plugin fixture utility already tolerates it: an
            // archive handle held open by the framework must not make teardown
            // blame this class.
        }
    }

    /// <remarks>
    /// The whole point of the file. <c>Execute</c> parses and
    /// <c>Run</c> dispatches, exactly as <c>Program</c> arranges them — the only
    /// substitution is the environment, so the clock, the console and the
    /// install root are the test's rather than the machine's.
    /// </remarks>
    private int Invoke(params string[] args) => PreflightCommandLine.Execute(
        args,
        _output,
        _error,
        parse => CommandDispatcher.Run(parse, Environment()));

    private CommandEnvironment Environment() => CommandEnvironments.For(
        _workspace,
        _output,
        _error,
        TimeProvider.System,
        installRoot: new PipelineInstallRoot(_installRoot));

    /// <summary>Writes a pipeline source tree and returns its directory.</summary>
    private DirectoryInfo SourceTree(string name = "projecta", string version = "1.4.0")
    {
        var tree = _root.CreateSubdirectory($"{name}-src");

        WorkspaceFiles.Write(tree, PackageManifest.FileName, $$"""
            {
              "schemaVersion": 1,
              "name": "{{name}}",
              "version": "{{version}}",
              "policyFile": "preflight.{{name}}.json",
              "ruleAssemblies": [],
              "abstractionsMinimumVersion": "{{ContractVersion.Current}}"
            }
            """);

        WorkspaceFiles.Write(tree, $"preflight.{name}.json", """{ "schemaVersion": 1 }""");

        return tree;
    }

    /// <summary>Packs a tree and installs it, through the dispatch both times.</summary>
    private string GivenAnInstalledPackage(string name = "projecta", string version = "1.4.0")
    {
        var package = Path.Combine(_root.FullName, $"{name}-{version}.zip");

        Invoke("pipeline", "pack", SourceTree(name, version).FullName, "-o", package)
            .ShouldBe(0);
        Invoke("pipeline", "install", package).ShouldBe(0);

        return package;
    }


    [Fact]
    public void CreateRule_ReachesTheHandlerWithTheIdItWasGiven()
    {
        Invoke("create", "rule", "acme.textures.dimension").ShouldBe(0);

        // The id decides every generated name, so a directory by this name is
        // proof that the argument arrived rather than that an arm was reached.
        Directory.Exists(Path.Combine(_workspace.FullName, "Acme.Textures.Dimension"))
            .ShouldBeTrue();
    }

    [Fact]
    public void CreateRule_ForAnIdThatIsNotARuleId_IsTwoRatherThanAnUnhandledException()
    {
        Invoke("create", "rule", "Acme.Textures").ShouldBe(2);

        _error.ToString().ShouldContain("Acme.Textures");
    }

    [Fact]
    public void CreatePolicy_ReachesTheHandlerWithTheNameItWasGiven()
    {
        Invoke("create", "policy", "projecta").ShouldBe(0);

        File.Exists(Path.Combine(_workspace.FullName, "preflight.projecta.json")).ShouldBeTrue();
    }

    [Fact]
    public void PipelineDeclare_ReachesTheHandlerWithTheNameItWasGiven()
    {
        Invoke("pipeline", "declare", "projecta").ShouldBe(0);

        File.ReadAllText(Path.Combine(_workspace.FullName, PolicyResolution.BaseFileName))
            .ShouldContain("\"pipeline\": \"projecta\"");
    }

    /// <remarks>
    /// The argument is optional, so the parser hands the handler
    /// <see langword="null"/> rather than refusing — and the handler is what
    /// decides there is nobody to ask. Getting the arity wrong here would make
    /// the command unusable in the one shape a build agent uses it in.
    /// </remarks>
    [Fact]
    public void PipelineDeclare_WithoutAName_ReachesTheHandlerAndIsRefusedThere()
    {
        Invoke("pipeline", "declare").ShouldBe(2);

        _error.ToString().ShouldContain("pipeline");
        File.Exists(Path.Combine(_workspace.FullName, PolicyResolution.BaseFileName)).ShouldBeFalse();
    }

    [Fact]
    public void PipelineUse_ReachesTheHandlerWithTheSelectorItWasGiven()
    {
        GivenAnInstalledPackage();

        Invoke("pipeline", "use", "projecta@1.4.0").ShouldBe(0);

        new MachineStateStore()
            .Read(new PipelineInstallRoot(_installRoot).MachineStatePath)
            .Pins["projecta"].ToString()
            .ShouldBe("1.4.0");
    }

    [Fact]
    public void PipelineUse_WithoutASelector_ReachesTheHandlerAndIsRefusedThere()
    {
        GivenAnInstalledPackage();

        Invoke("pipeline", "use").ShouldBe(2);

        File.Exists(new PipelineInstallRoot(_installRoot).MachineStatePath).ShouldBeFalse();
    }

    [Fact]
    public void PipelineList_ReachesTheHandler()
    {
        GivenAnInstalledPackage();

        Invoke("pipeline", "list").ShouldBe(0);

        _output.ToString().ShouldContain("projecta");
        _output.ToString().ShouldContain("1.4.0");
    }

    /// <remarks>
    /// Two argument names in one invocation, and <c>-o</c> is the only aliased
    /// option the phase added. A disagreement on either is a runtime throw on
    /// the first real use.
    /// </remarks>
    [Fact]
    public void PipelinePack_ReachesTheHandlerWithBothTheSourceAndTheOutput()
    {
        var output = Path.Combine(_root.FullName, "packed.zip");

        Invoke("pipeline", "pack", SourceTree().FullName, "-o", output).ShouldBe(0);

        File.Exists(output).ShouldBeTrue();
    }

    [Fact]
    public void PipelinePack_WithTheLongSpellingOfTheOutputOption_ReachesTheSameHandler()
    {
        var output = Path.Combine(_root.FullName, "packed-long.zip");

        Invoke("pipeline", "pack", SourceTree().FullName, "--output", output).ShouldBe(0);

        File.Exists(output).ShouldBeTrue();
    }

    /// <remarks>
    /// Required, and refused by the parser rather than by the handler — which is
    /// exit 2 by the same path every parse error takes. A <c>pack</c> with
    /// nowhere to write is not a command with a default.
    /// </remarks>
    [Fact]
    public void PipelinePack_WithoutAnOutput_IsTwoAndNamesTheOption()
    {
        Invoke("pipeline", "pack", SourceTree().FullName).ShouldBe(2);

        _error.ToString().ShouldContain("--output");
    }

    [Fact]
    public void PipelineValidate_ReachesTheHandlerWithTheSourceItWasGiven()
    {
        Invoke("pipeline", "validate", SourceTree().FullName).ShouldBe(0);

        _output.ToString().ShouldContain("projecta@1.4.0");
    }

    /// <remarks>
    /// <c>validate</c> is the one subcommand of <c>pipeline</c> that both
    /// discovers rules and resolves a policy, so it is the one that carries the
    /// flag. Passing it here has to reach the handler rather than the parser's
    /// "unrecognised option" path.
    /// </remarks>
    [Fact]
    public void PipelineValidate_WithRulesPath_ReachesTheHandler()
    {
        var empty = _root.CreateSubdirectory("no-plugins");

        Invoke("pipeline", "validate", SourceTree().FullName, "--rules-path", empty.FullName)
            .ShouldBe(0);

        _error.ToString().ShouldNotContain("rules-path");
    }

    [Fact]
    public void PipelineInstall_ReachesTheHandlerWithThePackageItWasGiven()
    {
        var package = Path.Combine(_root.FullName, "installable.zip");

        Invoke("pipeline", "pack", SourceTree().FullName, "-o", package).ShouldBe(0);
        Invoke("pipeline", "install", package).ShouldBe(0);

        var installed = new PipelineInstallRoot(_installRoot).VersionDirectory(
            "projecta", Version("1.4.0"));

        File.Exists(Path.Combine(installed.FullName, PackageManifest.FileName)).ShouldBeTrue();
    }

    /// <remarks>
    /// The two options the arm reads by name beside the argument. Passed
    /// together on purpose: <c>--no-gc</c> makes <c>--keep</c> decide nothing,
    /// so what this asserts is that both names parse and reach the handler, not
    /// what retention then did — which is fixed where retention is tested.
    /// </remarks>
    [Fact]
    public void PipelineInstall_WithKeepAndNoGc_ReachesTheHandlerWithBoth()
    {
        var package = Path.Combine(_root.FullName, "with-options.zip");

        Invoke("pipeline", "pack", SourceTree().FullName, "-o", package).ShouldBe(0);
        Invoke("pipeline", "install", package, "--keep", "1", "--no-gc").ShouldBe(0);

        _error.ToString().ShouldBeEmpty();
        _output.ToString().ShouldContain("Installed projecta@1.4.0");
    }

    /// <summary>
    /// Every <c>pipeline</c> subcommand, in one place, refusing what it should
    /// refuse.
    /// </summary>
    /// <remarks>
    /// The complement of the cases above. Each of those proves an arm is reached
    /// on the happy path; this proves the refusals travel back out through the
    /// same boundary and become exit 2 rather than an unhandled exception —
    /// which is the difference between "the workspace is wrong" and "this tool
    /// broke", and decides who gets called.
    /// </remarks>
    [Theory]
    [InlineData("create|policy|base")]
    [InlineData("pipeline|use|projecta@9.9.9")]
    [InlineData("pipeline|pack|nowhere|-o|out.zip")]
    [InlineData("pipeline|validate|nowhere")]
    [InlineData("pipeline|install|nowhere.zip")]
    public void ForEveryRefusalOfAPipelineSubcommand_TheBoundaryTurnsItIntoTwo(string arguments)
    {
        Invoke(arguments.Split('|')).ShouldBe(2);

        _error.ToString().ShouldNotBeEmpty();
    }

    private static PackageVersion Version(string text)
    {
        PackageVersion.TryParse(text, out var version).ShouldBeTrue();

        return version!;
    }
}
