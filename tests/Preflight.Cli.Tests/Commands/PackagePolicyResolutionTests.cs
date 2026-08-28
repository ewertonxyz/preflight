namespace Preflight.Cli.Tests.Commands;

using System.Text;
using Preflight.Cli.Commands;
using Preflight.TestSupport;

/// <summary>
/// Runs the commands against a policy that came out of an installed package,
/// rather than out of the checkout.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of phase 10 that changes what a run is configured by, and
/// until now nothing exercised it: every policy test in the suite writes its
/// files into the workspace, so the package arm of the chain — the qualified
/// provenance, the package's own <c>extends</c>, the seals it declares — was
/// reached by no test at all. The failure that hides there is the expensive one:
/// a package whose policy silently did not apply is a run reporting success
/// having checked less than the studio asked for.
/// </para>
/// <para>
/// Everything here goes through the real parser and the real dispatch, packing
/// and installing a real archive first, because the package arm is only entered
/// when <c>PackageResolution</c> resolved one — and that is a decision made at
/// the dispatch point, not something a test can hand to a handler.
/// </para>
/// </remarks>
public sealed class PackagePolicyResolutionTests : IDisposable
{
    private const string LargeFile = "core.presubmit.large-file";

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("preflight-pkg-policy-");
    private readonly DirectoryInfo _workspace;
    private readonly DirectoryInfo _installRoot;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public PackagePolicyResolutionTests()
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
            // Tolerated, as the plugin fixture utility already tolerates it.
        }
    }

    private int Invoke(params string[] args) => PreflightCommandLine.Execute(
        args,
        _output,
        _error,
        parse => PreflightCommandLine.Run(parse, Environment()));

    private CommandEnvironment Environment() => CommandEnvironments.For(
        _workspace,
        _output,
        _error,
        TimeProvider.System,
        installRoot: new PipelineInstallRoot(_installRoot));

    private static void Write(DirectoryInfo directory, string relativePath, string content)
    {
        var path = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
    }

    /// <summary>
    /// Packs and installs a package carrying <paramref name="policy"/>, and
    /// points the checkout at it.
    /// </summary>
    /// <remarks>
    /// The checkout gets only the two keys it owns — which pipeline it is, and
    /// which versions it accepts. It deliberately does not get a
    /// <c>preflight.projecta.json</c>: the package's policy is the entry of the
    /// chain, standing where that file used to, and a workspace file beside it
    /// is exit 2 rather than a merge.
    /// </remarks>
    private void GivenAnInstalledPipeline(
        string policy, IReadOnlyDictionary<string, string>? extraFiles = null)
    {
        var tree = _root.CreateSubdirectory("projecta-src");

        Write(tree, PackageManifest.FileName, $$"""
            {
              "schemaVersion": 1,
              "name": "projecta",
              "version": "1.4.0",
              "policyFile": "preflight.projecta.json",
              "ruleAssemblies": [],
              "abstractionsMinimumVersion": "{{ContractVersion.Current}}"
            }
            """);

        Write(tree, "preflight.projecta.json", policy);

        foreach (var (relative, content) in extraFiles ?? new Dictionary<string, string>())
        {
            Write(tree, relative, content);
        }

        var package = Path.Combine(_root.FullName, "projecta-1.4.0.zip");

        Invoke("pipeline", "pack", tree.FullName, "-o", package).ShouldBe(0);
        Invoke("pipeline", "install", package).ShouldBe(0);

        Write(_workspace, PolicyResolution.BaseFileName, """
            {
              "schemaVersion": 1,
              "pipeline": "projecta",
              "requiresPipeline": { "minimumVersion": "1.0.0", "maximumVersion": "2.0.0" }
            }
            """);
    }

    private string Output()
    {
        var text = _output.ToString();

        // The pack and install lines belong to the arrangement, not to the
        // assertion, and leaving them in makes every ShouldContain here weaker
        // than it reads.
        var marker = text.LastIndexOf("The pin is unchanged", StringComparison.Ordinal);

        return marker < 0 ? text : text[marker..];
    }

    /// <summary>
    /// The package's policy is the entry of the chain, and its values apply.
    /// </summary>
    /// <remarks>
    /// The exact point where "a named pipeline whose file is absent is exit 2,
    /// never a silent fallback" either survives the arrival of packages or turns
    /// into a silent fallback *to* the package. It survives because the package
    /// replaces the file rather than standing behind it.
    /// </remarks>
    [Fact]
    public void Explain_WithAPackageAndNoWorkspaceFile_TakesTheValueFromThePackage()
    {
        GivenAnInstalledPipeline($$"""
            {
              "schemaVersion": 1,
              "rules": { "{{LargeFile}}": { "settings": { "maxBytes": 1234 } } }
            }
            """);

        Invoke("explain", LargeFile).ShouldBe(0);

        Output().ShouldContain("1234");
    }

    /// <remarks>
    /// The provenance, qualified. Without the package in front of it the reader
    /// goes looking in the checkout for a file that is not there — and two runs
    /// of one commit against two packages print the same bytes, which is
    /// principle nº1 failing with nothing on screen saying so. See ADR-034.
    /// </remarks>
    [Fact]
    public void Explain_WithAPackage_NamesTheOriginQualifiedByPackageAndVersion()
    {
        GivenAnInstalledPipeline($$"""
            {
              "schemaVersion": 1,
              "rules": { "{{LargeFile}}": { "settings": { "maxBytes": 1234 } } }
            }
            """);

        Invoke("explain", LargeFile).ShouldBe(0);

        Output().ShouldContain("projecta@1.4.0/projecta.json");

        // Never the absolute install path: it carries the account name of
        // whoever ran the tool, and these strings reach a SARIF posted on a
        // merge request.
        Output().ShouldNotContain(_installRoot.FullName);
    }

    [Fact]
    public void Explain_WithAPackage_DoesNotSayDefaultsOnly()
    {
        GivenAnInstalledPipeline("""{ "schemaVersion": 1 }""");

        Invoke("explain", LargeFile).ShouldBe(0);

        Output().ShouldNotContain("defaults only");
        Output().ShouldContain("projecta");
    }

    /// <remarks>
    /// The overlay is the escape hatch and it still reaches over the package —
    /// otherwise a dev could not loosen anything on their own machine, which is
    /// what §6.3 exists to allow. What it must not do is disappear from the
    /// chain: a run configured by an unversioned file has to say so.
    /// </remarks>
    [Fact]
    public void Explain_WithAPackageAndALocalOverlay_ShowsBothInTheChain()
    {
        GivenAnInstalledPipeline($$"""
            {
              "schemaVersion": 1,
              "rules": { "{{LargeFile}}": { "settings": { "maxBytes": 1234 } } }
            }
            """);

        Write(_workspace, PolicyResolution.LocalFileName, $$"""
            {
              "schemaVersion": 1,
              "rules": { "{{LargeFile}}": { "settings": { "maxBytes": 4321 } } }
            }
            """);

        Invoke("explain", LargeFile).ShouldBe(0);

        Output().ShouldContain("projecta@1.4.0/projecta.json");
        Output().ShouldContain("local");
        Output().ShouldContain("4321");
    }

    /// <remarks>
    /// The seal is what the previous phase spent an ADR on, and a package is the
    /// first place a studio can actually put one. The message has to name the
    /// qualified path, because the file it points at is not in the checkout and
    /// "preflight.projecta.json:4" sends the reader somewhere that does not
    /// exist. See ADR-031.
    /// </remarks>
    [Fact]
    public void Explain_WithASealInThePackageViolatedByTheOverlay_IsTwoAndNamesTheQualifiedPath()
    {
        GivenAnInstalledPipeline($$"""
            {
              "schemaVersion": 1,
              "sealed": ["{{LargeFile}}:settings.maxBytes"],
              "rules": { "{{LargeFile}}": { "settings": { "maxBytes": 1234 } } }
            }
            """);

        Write(_workspace, PolicyResolution.LocalFileName, $$"""
            {
              "schemaVersion": 1,
              "rules": { "{{LargeFile}}": { "settings": { "maxBytes": 999999 } } }
            }
            """);

        Invoke("explain", LargeFile).ShouldBe(2);

        _error.ToString().ShouldContain("projecta@1.4.0/");

        // The seal violation is reported from the declaring document's own path
        // rather than from a policy origin, which is a second door into the
        // absolute install path ADR-034 nº7 forbids — and it was open until this
        // test was written. The overlay's own path is a workspace path and
        // belongs in the message; the package's does not.
        _error.ToString().ShouldNotContain(_installRoot.FullName);
        _error.ToString().ShouldContain(PolicyResolution.LocalFileName);
    }

    /// <remarks>
    /// A package that vendors its baseline extends it by a relative path inside
    /// the version directory, which is the arrangement ADR-033 nº14 chose over a
    /// dependency between packages. That it works is worth a test; that it works
    /// with the provenance qualified is the point.
    /// </remarks>
    [Fact]
    public void Explain_WithAPackagePolicyExtendingAVendoredBaseline_ResolvesInsideTheVersionDirectory()
    {
        GivenAnInstalledPipeline(
            $$"""
            {
              "schemaVersion": 1,
              "extends": "baseline.json",
              "rules": { "{{LargeFile}}": { "blocking": false } }
            }
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["baseline.json"] = $$"""
                    {
                      "schemaVersion": 1,
                      "rules": { "{{LargeFile}}": { "settings": { "maxBytes": 777 } } }
                    }
                    """,
            });

        Invoke("explain", LargeFile).ShouldBe(0);

        Output().ShouldContain("777");
        Output().ShouldContain("projecta@1.4.0/baseline.json");
    }

    /// <remarks>
    /// An <c>extends</c> that climbs out of the version directory is the package
    /// reaching for a file the install root does not own. Refused, and refused
    /// before anything executes.
    /// </remarks>
    [Fact]
    public void Explain_WithAPackagePolicyExtendingOutsideTheVersionDirectory_IsTwo()
    {
        GivenAnInstalledPipeline("""
            {
              "schemaVersion": 1,
              "extends": "../../../elsewhere.json"
            }
            """);

        Invoke("explain", LargeFile).ShouldBe(2);

        _error.ToString().ShouldNotBeEmpty();
    }

    /// <summary>
    /// A workspace file beside a resolved package is a refusal, not a merge.
    /// </summary>
    /// <remarks>
    /// Two sources for the entry of the chain, and nothing saying which wins.
    /// The refusal is the whole of §4.3.1 line 2, and it is the one row of that
    /// matrix a real workspace reaches by accident — by keeping the file the
    /// project used before the pipeline was packaged.
    /// </remarks>
    [Fact]
    public void Run_WithBothAPackageRequirementAndAWorkspacePolicyFile_IsTwo()
    {
        GivenAnInstalledPipeline("""{ "schemaVersion": 1 }""");

        Write(_workspace, "preflight.projecta.json", """{ "schemaVersion": 1 }""");

        Invoke("run", "--stage", "workspace").ShouldBe(2);

        _error.ToString().ShouldNotBeEmpty();
    }

    /// <remarks>
    /// The header is the surface ADR-034 exists for. `projecta` alone would make
    /// two runs against two packages indistinguishable; `projecta@1.4.0` is what
    /// makes them differ, and the reason in parentheses is what makes the
    /// difference explainable.
    /// </remarks>
    [Fact]
    public void Run_WithAPackage_PutsTheNameAtVersionAndTheReasonInTheHeader()
    {
        GivenAnInstalledPipeline("""{ "schemaVersion": 1 }""");

        Invoke("run", "--stage", "workspace");

        Output().ShouldContain("projecta@1.4.0");
        Output().ShouldContain($"from {PipelineRequirement.KeyName}");
    }

    [Fact]
    public void Run_WithAPinnedPackage_SaysPinnedRatherThanNamingTheRequirement()
    {
        GivenAnInstalledPipeline("""{ "schemaVersion": 1 }""");

        Invoke("pipeline", "use", "projecta@1.4.0").ShouldBe(0);
        Invoke("run", "--stage", "workspace");

        Output().ShouldContain("projecta@1.4.0 (pinned)");
    }

    /// <remarks>
    /// No requirement and no pin: the newest installed version, and the header
    /// says which of the three reasons it was. This is the arm the reporter
    /// falls to, and the one a checkout that never declared a range gets.
    /// </remarks>
    [Fact]
    public void Run_WithNeitherAPinNorARequirement_SaysNewestInstalled()
    {
        GivenAnInstalledPipeline("""{ "schemaVersion": 1 }""");

        Write(_workspace, PolicyResolution.BaseFileName, """
            { "schemaVersion": 1, "pipeline": "projecta" }
            """);

        Invoke("run", "--stage", "workspace");

        // Not "newest installed": the checkout named the pipeline, and the
        // header says so rather than repeating what the resolver did. The two
        // read the same to a machine and differently to a person, and the person
        // is who the header is for.
        Output().ShouldContain($"projecta@1.4.0 (from {PolicyResolution.BaseFileName})");
    }

    /// <remarks>
    /// The other half of that arm. With the pipeline named on the command line
    /// there is nothing to attribute the choice to, so the header says which
    /// version was picked and why — the only one of the three reasons that is
    /// about the version rather than about the name.
    /// </remarks>
    [Fact]
    public void Run_WithThePipelineNamedOnTheCommandLine_SaysNewestInstalled()
    {
        GivenAnInstalledPipeline("""{ "schemaVersion": 1 }""");

        File.Delete(Path.Combine(_workspace.FullName, PolicyResolution.BaseFileName));

        Invoke("run", "--stage", "workspace", "--pipeline", "projecta");

        Output().ShouldContain("projecta@1.4.0 (newest installed)");
    }

    /// <summary>
    /// The package's own <c>rules/</c> directory joins the load path.
    /// </summary>
    /// <remarks>
    /// A package brings rules, and a run that loaded its policy without its
    /// assemblies would reject every policy key naming one of them with "unknown
    /// rule id" — the misleading second error, arrived at through the package
    /// door. The directory existing is what the composition checks, so a package
    /// carrying one has to reach a different branch from a package without.
    /// </remarks>
    [Fact]
    public void Rules_WithAPackageCarryingARulesDirectory_ComposesWithoutRefusing()
    {
        GivenAnInstalledPipeline(
            """{ "schemaVersion": 1 }""",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Not an assembly, and deliberately not named *.dll: what is
                // being exercised is the branch that adds the directory to the
                // probe, and a broken assembly inside it would exercise the
                // loader's refusal instead.
                ["rules/README.txt"] = "the package's rules live here",
            });

        Invoke("rules").ShouldBe(0);

        Output().ShouldContain(LargeFile);
    }

    /// <remarks>
    /// A package and a <c>--rules-path</c> at once, which is what an author
    /// debugging their own rule against the studio's package actually types. The
    /// two probe paths are combined into one resolution rather than two, so a
    /// rule id colliding across them is the ordinary collision with nobody
    /// winning — the load-order rule ADR-025 refuses.
    /// </remarks>
    [Fact]
    public void Rules_WithAPackageAndARulesPath_CombinesBothIntoOneProbe()
    {
        GivenAnInstalledPipeline("""{ "schemaVersion": 1 }""");

        var extra = _root.CreateSubdirectory("extra-rules");

        Invoke("rules", "--rules-path", extra.FullName).ShouldBe(0);

        _error.ToString().ShouldNotContain("rules-path");
        Output().ShouldContain(LargeFile);
    }
}
