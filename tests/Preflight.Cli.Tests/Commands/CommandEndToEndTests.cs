namespace Preflight.Cli.Tests.Commands;

using System.Text;
using NSubstitute;
using Preflight.Abstractions;
using Preflight.Cli.Commands;
using Preflight.Core;
using Preflight.Core.History;
using Preflight.Core.Policy;

/// <summary>
/// Runs the four commands of the command surface end to end, against a real
/// workspace on disk and the six real rules.
/// </summary>
/// <remarks>
/// <para>
/// Everything below the command surface has its own tests. What none of them
/// can show is whether the pieces were connected in the right order — a policy
/// chain assembled with the layers reversed, a reporter handed the wrong glyph
/// set, a stage that never reaches the executor. Each of those leaves every
/// other test green.
/// </para>
/// <para>
/// The environment is injected rather than the process spawned, so the exit
/// code, standard output and standard error are all readable and the clock and
/// run id are fixed. The byte-identical guarantee needs the last two; the
/// <c>Preflight.Specs</c> project spawns the real binary for the contract a
/// process has and an in-process call cannot observe.
/// </para>
/// </remarks>
public sealed class CommandEndToEndTests : IDisposable
{
    private readonly DirectoryInfo _workspace = Directory.CreateTempSubdirectory("preflight-e2e-");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public void Dispose()
    {
        _workspace.Delete(recursive: true);
        _output.Dispose();
        _error.Dispose();
    }

    private int Invoke(params string[] args) => PreflightCommandLine.Execute(
        args,
        _output,
        _error,
        parse => PreflightCommandLine.Run(parse, Injected()));

    /// <summary>
    /// The real machine, with everything a test needs to see replaced.
    /// </summary>
    /// <remarks>
    /// Delegated to <see cref="CommandEnvironments"/> so that a member added to
    /// <c>CommandEnvironment</c> gets its test default in one place rather than
    /// in every class that builds one. The history goes to a real store under
    /// the temporary workspace; xUnit builds a fresh instance of this class per
    /// test method, so the workspace — and therefore the history — is already
    /// isolated per test.
    /// </remarks>
    private CommandEnvironment Injected(
        IReadOnlyList<IValidationRule>? rules = null,
        IEnvironmentReader? reader = null,
        IHistoryStore? history = null) =>
        CommandEnvironments.For(
            _workspace,
            _output,
            _error,
            TimeProvider.System,
            rules,
            reader,
            history);

    private static IEnvironmentReader NoCiEnvironment()
    {
        var environment = Substitute.For<IEnvironmentReader>();

        environment.GetVariable(Arg.Any<string>()).Returns((string?)null);

        return environment;
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_workspace.FullName, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>
    /// A workspace every rule is satisfied by.
    /// </summary>
    private void GivenAGoodWorkspace()
    {
        Write("preflight.workspace.json", """
            {
              "tools": [
                { "name": "git", "command": "git", "arguments": ["--version"], "minimumVersion": "2.0.0" }
              ]
            }
            """);
    }

    [Fact]
    public void Run_OverAWorkspaceThatSatisfiesEveryRule_IsZeroAndPrintsTheReport()
    {
        GivenAGoodWorkspace();

        Invoke("run", "--stage", "workspace").ShouldBe(0);

        _output.ToString().ShouldContain("core.workspace.toolchain");
        _output.ToString().ShouldContain("Passed");
    }

    /// <remarks>
    /// The whole tool, in one assertion: a rule fails, the verdict is
    /// <c>Blocked</c>, and the exit-code contract maps that to the code a
    /// pipeline reads as "the commit's author has something to fix".
    /// </remarks>
    [Fact]
    public void Run_WhenABlockingRuleFails_IsOne()
    {
        Write("preflight.workspace.json", """
            {
              "tools": [
                { "name": "git", "command": "git", "arguments": ["--version"], "minimumVersion": "999.0.0" }
              ]
            }
            """);

        Invoke("run", "--stage", "workspace").ShouldBe(1);

        _output.ToString().ShouldContain("Blocked");
    }

    /// <summary>
    /// A stage no rule answers to is a configuration error.
    /// </summary>
    /// <remarks>
    /// Not a green run over an empty set. The user asked a question the rule
    /// set cannot answer, and nobody decided that — which is what separates it
    /// from the second row below.
    /// </remarks>
    [Fact]
    public void Run_ForAStageNoRuleAnswersTo_IsTwo()
    {
        // Only the pre-submit rules, so the workspace stage has nothing.
        var environment = Injected(
            [.. Preflight.Rules.Tests.BuiltInRuleDescriptorsTests.Discovered()
                .Where(rule => rule.Descriptor.Stage == ValidationStage.PreSubmit)]);

        PreflightCommandLine.Execute(
            ["run", "--stage", "workspace"],
            _output,
            _error,
            parse => PreflightCommandLine.Run(parse, environment))
            .ShouldBe(2);

        _error.ToString().ShouldContain("No rule has stage 'workspace'");
    }

    /// <summary>
    /// Everything disabled is exit 0, said out loud.
    /// </summary>
    /// <remarks>
    /// Somebody wrote that decision in a versioned file, so refusing the run
    /// would turn a legitimate configuration into an obstacle — disabling a
    /// rule is already refused that. What must not happen is the report reading
    /// as an ordinary success: a <c>--set</c> typo or a copied overlay would
    /// otherwise keep a pipeline green indefinitely.
    /// </remarks>
    [Fact]
    public void Run_WithEveryRuleOfTheStageDisabled_IsZeroAndSaysNothingRan()
    {
        GivenAGoodWorkspace();
        Write("preflight.base.json", """
            {
              "schemaVersion": 1,
              "rules": {
                "core.workspace.toolchain": { "enabled": false },
                "core.workspace.dependencies": { "enabled": false }
              }
            }
            """);

        Invoke("run", "--stage", "workspace").ShouldBe(0);

        _output.ToString().ShouldContain("0 rules executed");
    }

    /// <remarks>
    /// The precedence chain of policy precedence, end to end: the base sets a
    /// limit, the production overlay raises it, and the run that would have
    /// failed passes. A chain assembled in the wrong order passes every unit
    /// test of the merge and fails exactly here.
    /// </remarks>
    [Fact]
    public void Run_WithAPipelineOverlay_AppliesItOverTheBase()
    {
        Write("preflight.base.json", """
            { "schemaVersion": 1, "rules": { "core.workspace.toolchain": { "enabled": true } } }
            """);
        Write("preflight.atlas.json", """
            {
              "schemaVersion": 1,
              "extends": "preflight.base.json",
              "rules": { "core.workspace.toolchain": { "enabled": false } }
            }
            """);
        GivenAGoodWorkspace();

        Invoke("run", "--stage", "workspace", "--pipeline", "atlas").ShouldBe(0);

        _output.ToString().ShouldContain("base");
        _output.ToString().ShouldContain("atlas");
    }

    /// <summary>
    /// <c>explain</c> names the target block a value came from.
    /// </summary>
    /// <remarks>
    /// Obligatory rather than nice: a layer that changes a number without
    /// saying so turns the one command that answers "why is this limit 4096"
    /// into a command that answers it wrongly.
    /// </remarks>
    [Fact]
    public void Explain_ForAValueFromATargetBlock_NamesTheTargetKeyAndTheFile()
    {
        Write("preflight.base.json", """
            {
              "schemaVersion": 1,
              "rules": { "core.presubmit.large-file": { "blocking": true } },
              "targets": {
                "switch2": { "rules": { "core.presubmit.large-file": { "blocking": false } } }
              }
            }
            """);

        Invoke("explain", "core.presubmit.large-file", "--platform", "switch2").ShouldBe(0);

        _output.ToString().ShouldContain("(target switch2)");
    }

    /// <summary>
    /// Two platforms, one policy file, two different effective values.
    /// </summary>
    /// <remarks>
    /// The proof that the flags added to the inspection commands actually
    /// reach the merge: without them both invocations would print the same
    /// thing, and both would be describing a policy no run uses.
    /// </remarks>
    [Fact]
    public void Explain_WithADifferentPlatform_ShowsADifferentEffectiveValue()
    {
        Write("preflight.base.json", """
            {
              "schemaVersion": 1,
              "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 1048576 } } },
              "targets": {
                "switch2": {
                  "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 524288 } } }
                }
              }
            }
            """);

        Invoke("explain", "core.presubmit.large-file", "--platform", "switch2").ShouldBe(0);

        var onSwitch = _output.ToString();

        _output.GetStringBuilder().Clear();

        Invoke("explain", "core.presubmit.large-file", "--platform", "ps5").ShouldBe(0);

        _output.ToString().ShouldNotBe(onSwitch);
    }

    /// <remarks>
    /// A platform no block mentions is the common case, not a mistake. ADR-015
    /// is about refusing what the tool does not understand, and this is
    /// understood perfectly: there is nothing to apply.
    /// </remarks>
    [Fact]
    public void Explain_WithAPlatformNoTargetBlockMentions_SaysNothingAboutTargets()
    {
        Write("preflight.base.json", """
            {
              "schemaVersion": 1,
              "rules": { "core.presubmit.large-file": { "blocking": true } },
              "targets": {
                "switch2": { "rules": { "core.presubmit.large-file": { "blocking": false } } }
              }
            }
            """);

        Invoke("explain", "core.presubmit.large-file", "--platform", "ps5").ShouldBe(0);

        _output.ToString().ShouldNotContain("(target");
    }

    /// <summary>
    /// <c>create workspace</c> writes the manifest, and then refuses to.
    /// </summary>
    /// <remarks>
    /// Both halves in one method on purpose: the second invocation is the
    /// interesting one, and it can only be trusted if the first really wrote
    /// the file this one is refusing to replace. The real writer, not a
    /// substitute — what the handler does with a fake is asserted in
    /// <c>CreateCommandTests</c>, and what is left untested by that is exactly
    /// whether the two halves agree about the path. See ADR-028.
    /// </remarks>
    [Fact]
    public void CreateWorkspace_WritesTheManifestOnceAndThenRefuses()
    {
        var manifest = Path.Combine(_workspace.FullName, "preflight.workspace.json");

        Invoke("create", "workspace").ShouldBe(0);
        File.Exists(manifest).ShouldBeTrue();

        var written = File.ReadAllBytes(manifest);

        Invoke("create", "workspace").ShouldBe(2);

        File.ReadAllBytes(manifest).ShouldBe(written);
        _error.ToString().ShouldContain("preflight.workspace.json");
    }

    /// <summary>
    /// The manifest the command writes is one the workspace stage accepts.
    /// </summary>
    /// <remarks>
    /// The lacuna this command exists to close: before it, a project that had
    /// never seen the tool got <c>Blocked</c> on its first run, because the
    /// toolchain rule fails on a missing manifest by design. A skeleton
    /// declaring no tools is a different fact — somebody said in writing there
    /// is nothing to check — and both workspace rules answer <c>n/a</c>.
    /// </remarks>
    [Fact]
    public void CreateWorkspace_ThenRunWorkspace_IsNotApplicableRatherThanBlocked()
    {
        Invoke("create", "workspace").ShouldBe(0);

        Invoke("run", "--stage", "workspace").ShouldBe(0);

        _output.ToString().ShouldContain("n/a");
        _output.ToString().ShouldNotContain("FAIL");
    }

    /// <summary>
    /// The deprecated spelling resolves the same file as the current one.
    /// </summary>
    /// <remarks>
    /// The alias is only worth keeping if it means the same thing, and the only
    /// way to show that is to make it do the same work: the same overlay, over
    /// the same base, with the same result. Asserting that the flag merely
    /// parses would pass just as well against an option nothing reads.
    /// See ADR-027.
    /// </remarks>
    [Fact]
    public void Run_WithTheDeprecatedProductionFlag_ResolvesTheSameOverlayAsPipeline()
    {
        Write("preflight.base.json", """
            { "schemaVersion": 1, "rules": { "core.workspace.toolchain": { "enabled": true } } }
            """);
        Write("preflight.atlas.json", """
            {
              "schemaVersion": 1,
              "extends": "preflight.base.json",
              "rules": { "core.workspace.toolchain": { "enabled": false } }
            }
            """);
        GivenAGoodWorkspace();

        Invoke("run", "--stage", "workspace", "--production", "atlas").ShouldBe(0);

        _output.ToString().ShouldContain("base");
        _output.ToString().ShouldContain("atlas");
    }

    /// <remarks>
    /// Two spellings of one flag define no precedence between them, so picking
    /// by flag order would decide for the user which name they meant. The same
    /// refusal, and the same reason, as <c>--no-local</c> with
    /// <c>--allow-local</c>.
    /// </remarks>
    [Fact]
    public void Run_WithBothPipelineAndProduction_IsTwoNamingBoth()
    {
        GivenAGoodWorkspace();

        Invoke("run", "--stage", "workspace", "--pipeline", "atlas", "--production", "atlas").ShouldBe(2);

        _error.ToString().ShouldContain("--pipeline");
        _error.ToString().ShouldContain("--production");
    }

    /// <remarks>
    /// Falling back to the base would run a weaker set of checks than the
    /// pipeline asked for and call it a success — the false green of
    /// principle 7, produced by a typo in a CI argument.
    /// </remarks>
    [Fact]
    public void Run_WithAPipelineThatHasNoFile_IsTwo()
    {
        GivenAGoodWorkspace();

        Invoke("run", "--stage", "workspace", "--pipeline", "nosuch").ShouldBe(2);
    }

    /// <remarks>
    /// A pipeline name becomes part of a filename, so it is validated as a
    /// label. Left unvalidated it reads a file outside the workspace.
    /// </remarks>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("")]
    public void Run_WithAPipelineThatIsNotALabel_IsTwo(string pipeline)
    {
        GivenAGoodWorkspace();

        Invoke("run", "--stage", "workspace", "--pipeline", pipeline).ShouldBe(2);
    }

    /// <remarks>
    /// <c>--set</c> sits at the top of the precedence chain, and policy
    /// validation puts every configuration problem at load time. An unknown key
    /// reaching the merge would surface much later as a value silently ignored.
    /// </remarks>
    [Fact]
    public void Run_WithASetOverrideNamingAnUnknownKey_IsTwo()
    {
        GivenAGoodWorkspace();

        Invoke("run", "--stage", "workspace", "--set", "core.workspace.toolchain:blockng=false").ShouldBe(2);
    }

    [Fact]
    public void Run_WithASetOverride_AppliesIt()
    {
        Write("preflight.workspace.json", """
            {
              "tools": [
                { "name": "git", "command": "git", "arguments": ["--version"], "minimumVersion": "999.0.0" }
              ]
            }
            """);

        // Blocking off: the rule still fails, but the run is no longer blocked.
        Invoke("run", "--stage", "workspace", "--set", "core.workspace.toolchain:blocking=false").ShouldBe(0);
    }

    [Fact]
    public void Run_WithFormatJson_WritesOneParseableDocument()
    {
        GivenAGoodWorkspace();

        Invoke("run", "--stage", "workspace", "--format", "json").ShouldBe(0);

        Should.NotThrow(() => System.Text.Json.JsonDocument.Parse(_output.ToString()));
    }

    /// <summary>
    /// <c>--format sarif</c> reaches the reporter, with the descriptors it
    /// needs.
    /// </summary>
    /// <remarks>
    /// The wiring, which no test below the command surface can show: the SARIF
    /// document needs <c>DisplayName</c> and <c>Documentation</c>, and the rule
    /// descriptor keeps both on the descriptor rather than on the execution. A
    /// handler that reached the reporter without them would compile and produce
    /// a document missing every rule name.
    /// </remarks>
    [Fact]
    public void Run_WithFormatSarif_WritesASarifDocumentToStandardOutput()
    {
        GivenAGoodWorkspace();

        Invoke("run", "--stage", "workspace", "--format", "sarif").ShouldBe(0);

        var rendered = _output.ToString();

        Should.NotThrow(() => System.Text.Json.JsonDocument.Parse(rendered));

        System.Text.Json.JsonDocument.Parse(rendered)
            .RootElement
            .GetProperty("version")
            .GetString()
            .ShouldBe("2.1.0");

        _error.ToString().ShouldBeEmpty();
    }

    /// <summary>
    /// <c>console</c> is the default, byte for byte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one test that would notice if replacing the silent fallback in
    /// <c>RunOptionsFrom</c> with <c>AcceptOnlyFromAmong</c> had changed which
    /// reporter an absent <c>--format</c> selects. Everything else asserts on a
    /// format that was asked for.
    /// </para>
    /// <para>
    /// The clock is frozen rather than injected as the system one, because the
    /// per-rule durations of the console report are the only thing that varies
    /// between two identical console runs — the determinism guarantee says
    /// exactly that, and freezing the clock is the seam it says exists for
    /// this.
    /// </para>
    /// </remarks>
    [Fact]
    public void Run_WithFormatConsole_IsByteIdenticalToTheCommandWithoutAFormat()
    {
        GivenAGoodWorkspace();

        OnAFrozenClock("run", "--stage", "workspace").ShouldBe(0);
        var first = _output.ToString();

        _output.GetStringBuilder().Clear();

        OnAFrozenClock("run", "--stage", "workspace", "--format", "console").ShouldBe(0);

        _output.ToString().ShouldBe(first);
    }

    /// <summary>
    /// Two identical runs under <c>--format json</c> differ in the run id and
    /// in nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other half of the guard above, and the one that says the determinism
    /// guarantee's promise survives the command path rather than only the
    /// reporter: the <c>JsonReporter</c> golden files fix the shape of one
    /// document, and this fixes that two documents over the same workspace
    /// agree. A parser change that quietly sent <c>json</c> somewhere else, or
    /// a reporter that emitted in completion order, fails here.
    /// </para>
    /// <para>
    /// The run id is masked rather than fixed, because <c>--format</c> is a
    /// flag and the run id deliberately is not: <c>RunOptions.RunId</c> exists
    /// for tests and has no command line of its own, so that nobody pins it in
    /// a pipeline and loses the one field that tells two runs apart in the
    /// instrumentation 's history. The raw comparison below asserts the masking
    /// was not vacuous — the two ids really are different.
    /// </para>
    /// </remarks>
    [Fact]
    public void Run_WithFormatJson_IsByteIdenticalAcrossRunsApartFromTheRunId()
    {
        GivenAGoodWorkspace();

        OnAFrozenClock("run", "--stage", "workspace", "--format", "json").ShouldBe(0);
        var first = _output.ToString();

        _output.GetStringBuilder().Clear();

        OnAFrozenClock("run", "--stage", "workspace", "--format", "json").ShouldBe(0);
        var second = _output.ToString();

        second.ShouldNotBe(first);
        WithoutTheRunId(second).ShouldBe(WithoutTheRunId(first));
    }

    private static string WithoutTheRunId(string json) =>
        System.Text.RegularExpressions.Regex.Replace(
            json,
            "\"runId\": \"[0-9a-fA-F-]+\"",
            "\"runId\": \"<masked>\"");

    /// <remarks>
    /// A frozen clock, so that every duration is nought and the start time does
    /// not move. The byte-identical guarantee is qualified for exactly the
    /// durations and the run id, and names the <c>TimeProvider</c> seam as what
    /// makes the rest of it assertable.
    /// </remarks>
    private int OnAFrozenClock(params string[] args)
    {
        var clock = new Preflight.TestSupport.FixedTimeProvider(
            new DateTimeOffset(2026, 8, 26, 14, 30, 0, TimeSpan.Zero));

        return PreflightCommandLine.Execute(
            args,
            _output,
            _error,
            parse => PreflightCommandLine.Run(
                parse,
                CommandEnvironments.For(_workspace, _output, _error, clock)));
    }

    /// <remarks>
    /// The built-in rule set's own example, reaching the report: a pre-submit
    /// run over a commit with nothing the rules examine reports <c>n/a</c>, and
    /// the run still succeeds.
    /// </remarks>
    [Fact]
    public void Run_ForPreSubmitWithNoChangedFiles_ReportsNotApplicableAndSucceeds()
    {
        GivenAGoodWorkspace();

        // A ref that resolves to the current tree in a workspace with no
        // repository would fail; the temp directory has none, so the change
        // source refuses and the run is a configuration error. That is the
        // documented behaviour, and asserting it here is what proves the change
        // source is wired at all.
        Invoke("run", "--stage", "pre-submit", "--changed-from", "HEAD").ShouldBe(2);

        _error.ToString().ShouldNotBeEmpty();
    }

    [Fact]
    public void Rules_ListsEveryRuleWithItsLevel()
    {
        GivenAGoodWorkspace();

        Invoke("rules").ShouldBe(0);

        foreach (var rule in Preflight.Rules.Tests.BuiltInRuleDescriptorsTests.Discovered())
        {
            _output.ToString().ShouldContain(rule.Descriptor.Id.Value);
        }
    }

    /// <remarks>
    /// A disabled rule appears, marked. Dropping it would answer "which rules
    /// exist" with "which rules will run", and the gap between those two is
    /// what someone runs this command to see.
    /// </remarks>
    [Fact]
    public void Rules_ShowsADisabledRuleRatherThanHidingIt()
    {
        Write("preflight.base.json", """
            {
              "schemaVersion": 1,
              "rules": { "core.presubmit.large-file": { "enabled": false } }
            }
            """);

        Invoke("rules").ShouldBe(0);

        _output.ToString().ShouldContain("core.presubmit.large-file");
        _output.ToString().ShouldContain("disabled by policy");
    }

    [Fact]
    public void Graph_PrintsTheLevelsAndTheDependencies()
    {
        Invoke("graph").ShouldBe(0);

        var printed = _output.ToString();

        printed.ShouldContain("level 0");
        printed.ShouldContain("core.build.compile-probe");
        printed.ShouldContain("<- core.build.configuration");
    }

    /// <summary>
    /// <c>--format text</c> is the default, byte for byte.
    /// </summary>
    /// <remarks>
    /// The guard on turning the body of <c>Graph</c> into one arm of a switch.
    /// Without it, <c>--format</c> arriving at this command could reformat the
    /// output and only <see cref="Graph_PrintsTheLevelsAndTheDependencies"/>
    /// would be in a position to notice — by containment, which is to say it
    /// would not.
    /// </remarks>
    [Fact]
    public void Graph_WithFormatText_IsByteIdenticalToTheCommandWithoutAFormat()
    {
        Invoke("graph").ShouldBe(0);
        var first = _output.ToString();

        _output.GetStringBuilder().Clear();

        Invoke("graph", "--format", "text").ShouldBe(0);

        _output.ToString().ShouldBe(first);
    }

    /// <summary>
    /// <c>--format dot</c> writes a digraph over the six real rules.
    /// </summary>
    /// <remarks>
    /// The count of distinct quoted identifiers rather than a containment
    /// check, for the reason the plugin loader changed
    /// <see cref="Execute_ThroughTheRealEnvironment_RunsTheCommandOverTheBuiltInRulesAlone"/>:
    /// a containment assertion stays green while the drawing silently loses
    /// half the graph.
    /// </remarks>
    [Fact]
    public void Graph_WithFormatDot_WritesADigraphOverTheRealRules()
    {
        Invoke("graph", "--format", "dot").ShouldBe(0);

        var rendered = _output.ToString();

        rendered.TrimStart().ShouldStartWith("digraph");
        rendered.ShouldContain("rankdir=LR");
        rendered.ShouldContain("\"core.build.compile-probe\"");

        System.Text.RegularExpressions.Regex
            .Matches(rendered, "\"(?<id>[a-z0-9.-]+)\"")
            .Select(match => match.Groups["id"].Value)
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(Preflight.Rules.Tests.BuiltInRuleDescriptorsTests.Discovered().Count);
    }

    /// <remarks>
    /// The rule graph makes this command's output diffable, so two runs produce
    /// the same bytes — the property, not a sample of it.
    /// </remarks>
    [Fact]
    public void Graph_IsDeterministic()
    {
        Invoke("graph");
        var first = _output.ToString();

        _output.GetStringBuilder().Clear();
        Invoke("graph");

        _output.ToString().ShouldBe(first);
    }

    [Fact]
    public void Explain_PrintsTheEffectivePolicyWithItsOrigins()
    {
        Write("preflight.base.json", """
            {
              "schemaVersion": 1,
              "rules": {
                "core.presubmit.large-file": {
                  "blocking": true,
                  "settings": { "maxBytes": 5242880 }
                }
              }
            }
            """);

        Invoke("explain", "core.presubmit.large-file").ShouldBe(0);

        var printed = _output.ToString();

        printed.ShouldContain("core.presubmit.large-file");
        printed.ShouldContain("settings.maxBytes");
        printed.ShouldContain("5242880");
        printed.ShouldContain("base.json:");
        printed.ShouldContain("Policy chain");
        printed.ShouldContain("Local overlay");
    }

    /// <summary>
    /// The line policy precedence promises: which layer this value replaced,
    /// and what it said.
    /// </summary>
    /// <remarks>
    /// "Why is this limit 4096?" has to have an answer in one command rather
    /// than in an archaeology of JSON files. Without the overrides line, the
    /// command shows the winner and hides the argument.
    /// </remarks>
    [Fact]
    public void Explain_ShowsWhichLayerOverrodeWhich()
    {
        Write("preflight.base.json", """
            {
              "schemaVersion": 1,
              "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 5242880 } } }
            }
            """);
        Write("preflight.atlas.json", """
            {
              "schemaVersion": 1,
              "extends": "preflight.base.json",
              "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 52428800 } } }
            }
            """);

        Invoke("explain", "core.presubmit.large-file", "--pipeline", "atlas").ShouldBe(0);

        var printed = _output.ToString();

        printed.ShouldContain("52428800");
        printed.ShouldContain("overrides");
        printed.ShouldContain("5242880");
    }

    /// <remarks>
    /// A rule whose policy nobody wrote still explains: every value has an
    /// origin, and <c>RuleDescriptor default</c> is one of them.
    /// </remarks>
    [Fact]
    public void Explain_WithNoPolicyFiles_ShowsDescriptorDefaults()
    {
        Invoke("explain", "core.presubmit.large-file").ShouldBe(0);

        _output.ToString().ShouldContain("RuleDescriptor default");
        _output.ToString().ShouldContain("defaults only");
    }

    /// <remarks>
    /// "Why is this disabled" is exactly the question this command exists to
    /// answer, so a disabled rule explains rather than being refused.
    /// </remarks>
    [Fact]
    public void Explain_ForADisabledRule_StillExplains()
    {
        Write("preflight.base.json", """
            {
              "schemaVersion": 1,
              "rules": { "core.presubmit.large-file": { "enabled": false } }
            }
            """);

        Invoke("explain", "core.presubmit.large-file").ShouldBe(0);

        _output.ToString().ShouldContain("enabled");
        _output.ToString().ShouldContain("false");
    }

    [Fact]
    public void Explain_WithAnUnknownId_IsTwoAndSuggestsTheNearest()
    {
        Invoke("explain", "core.presubmit.large-fil").ShouldBe(2);

        _error.ToString().ShouldContain("core.presubmit.large-file");
    }

    /// <summary>
    /// A malformed id is exit 2, with no stack trace.
    /// </summary>
    /// <remarks>
    /// <c>RuleId</c> validates in its constructor and throws. Uncaught, this
    /// leaves the process at exit 3 with a stack trace — an internal error
    /// claimed for a typo the user can fix in a second, and by the exit-code
    /// contract that routes the incident to a different person.
    /// </remarks>
    [Fact]
    public void Explain_WithAMalformedId_IsTwoWithoutAStackTrace()
    {
        Invoke("explain", "Core.Presubmit.Large-File").ShouldBe(2);

        _error.ToString().ShouldNotContain("   at ");
    }

    [Fact]
    public void Explain_WithNoId_IsTwo()
    {
        Invoke("explain").ShouldBe(2);
    }

    [Fact]
    public void Explain_ShowsDependenciesAndDependents()
    {
        Invoke("explain", "core.build.configuration").ShouldBe(0);

        var printed = _output.ToString();

        printed.ShouldContain("core.workspace.toolchain");
        printed.ShouldContain("core.build.compile-probe");
    }

    /// <remarks>
    /// The explain command shows a <c>docs</c> line, and none of the six
    /// built-in rules has a documentation URL — this project has no wiki, and
    /// inventing a URL that resolves to nothing would be worse than omitting
    /// the line. The branch exists for plugins, and a rule
    /// supplied through the injected environment is how it gets exercised
    /// without one.
    /// </remarks>
    [Fact]
    public void Explain_ForARuleWithDocumentation_PrintsTheLink()
    {
        var documented = new DocumentedRule();

        var environment = Injected([documented]);

        PreflightCommandLine.Execute(
            ["explain", documented.Descriptor.Id.Value],
            _output,
            _error,
            parse => PreflightCommandLine.Run(parse, environment))
            .ShouldBe(0);

        _output.ToString().ShouldContain("https://wiki.invalid/rules/sample");
    }

    /// <remarks>
    /// The origin a <c>--set</c> value carries. The explain command names the
    /// command line as a layer, and it is the top of the precedence chain — the
    /// one whose provenance somebody is most likely to be surprised by.
    /// </remarks>
    [Fact]
    public void Explain_ForAValueFromTheCommandLine_SaysSo()
    {
        Invoke("explain", "core.presubmit.large-file", "--set", "core.presubmit.large-file:blocking=false")
            .ShouldBe(0);

        _output.ToString().ShouldContain("command line");
    }

    /// <remarks>
    /// The recursive origin of the explain command: a root
    /// <c>defaultTimeoutSeconds</c> cascading into a rule that never mentioned
    /// a timeout. Flattening it to the file alone drops the half that explains
    /// why the rule has a value nobody wrote for it.
    /// </remarks>
    [Fact]
    public void Explain_ForATimeoutCascadedFromTheRoot_NamesTheRootKey()
    {
        Write("preflight.base.json", """
            { "schemaVersion": 1, "defaultTimeoutSeconds": 30 }
            """);

        Invoke("explain", "core.presubmit.large-file").ShouldBe(0);

        var printed = _output.ToString();

        printed.ShouldContain("timeoutSeconds");
        printed.ShouldContain("root defaultTimeoutSeconds");
    }

    /// <remarks>
    /// A settings value that is a list, and one that is JSON null. Both are
    /// legal — the policy schema leaves <c>settings</c> uninspected — and a
    /// renderer that only knew scalars would print a type name where a value
    /// belongs.
    /// </remarks>
    [Fact]
    public void Explain_RendersListAndNullSettings()
    {
        Write("preflight.base.json", """
            {
              "schemaVersion": 1,
              "rules": {
                "core.presubmit.forbidden-paths": {
                  "settings": { "patterns": ["**/*.pfx", "**/.env"], "unset": null }
                }
              }
            }
            """);

        Invoke("explain", "core.presubmit.forbidden-paths").ShouldBe(0);

        var printed = _output.ToString();

        printed.ShouldContain("**/*.pfx");
        printed.ShouldContain("null");
    }

    [Fact]
    public void Explain_ForARuleWithNoNeighbours_PrintsADashRatherThanNothing()
    {
        Invoke("explain", "core.presubmit.large-file").ShouldBe(0);

        _output.ToString().ShouldContain("—");
    }

    /// <remarks>
    /// Policy validation asks for a suggestion when there is one, not for a
    /// suggestion at any cost. <c>SuggestionFinder</c> returns nothing above
    /// its threshold, and "did you mean ''" would be worse than none.
    /// </remarks>
    [Fact]
    public void Explain_WithAnIdNothingResembles_IsTwoWithoutASuggestion()
    {
        Invoke("explain", "zzz.yyy.xxx").ShouldBe(2);

        _error.ToString().ShouldNotContain("Did you mean");
    }

    /// <summary>
    /// The local overlay reaches the effective policy, and the header says so.
    /// </summary>
    /// <remarks>
    /// The local-overlay rule calls this an integrity hole worth announcing:
    /// <c>preflight.local.json</c> is unversioned, so a run it took part in is
    /// a run nobody reviewed. Without this test the overlay could be resolved
    /// correctly and never merged, and every unit test of the decision would
    /// still pass.
    /// </remarks>
    [Fact]
    public void Run_WithALocalOverlay_AppliesItAndAnnouncesIt()
    {
        Write("preflight.workspace.json", """
            {
              "tools": [
                { "name": "git", "command": "git", "arguments": ["--version"], "minimumVersion": "999.0.0" }
              ]
            }
            """);
        // A base as well, so the chain the overlay is appended to is not empty:
        // 'local' arriving on top of nothing and on top of a chain are two
        // different code paths through the same line.
        Write("preflight.base.json", """{ "schemaVersion": 1 }""");
        Write("preflight.local.json", """
            {
              "schemaVersion": 1,
              "rules": { "core.workspace.toolchain": { "enabled": false } }
            }
            """);

        Invoke("run", "--stage", "workspace").ShouldBe(0);

        _output.ToString().ShouldContain("local overlay active");
        _output.ToString().ShouldContain("base");
    }

    [Fact]
    public void Run_WithNoLocal_IgnoresTheOverlayAndSaysWhy()
    {
        Write("preflight.workspace.json", """
            {
              "tools": [
                { "name": "git", "command": "git", "arguments": ["--version"], "minimumVersion": "999.0.0" }
              ]
            }
            """);
        Write("preflight.local.json", """
            {
              "schemaVersion": 1,
              "rules": { "core.workspace.toolchain": { "enabled": false } }
            }
            """);

        Invoke("run", "--stage", "workspace", "--no-local").ShouldBe(1);

        _output.ToString().ShouldContain("--no-local");
    }

    /// <remarks>
    /// The explain command names the variable, because "CI detected" without it
    /// leaves somebody guessing which of five their agent sets.
    /// </remarks>
    [Fact]
    public void Explain_InsideCi_NamesTheDetectedVariable()
    {
        var environment = Substitute.For<IEnvironmentReader>();

        environment.GetVariable(Arg.Any<string>()).Returns((string?)null);
        environment.GetVariable("TEAMCITY_VERSION").Returns("2026.1");

        Write("preflight.local.json", """{ "schemaVersion": 1 }""");

        var command = Injected(reader: environment);

        PreflightCommandLine.Execute(
            ["explain", "core.presubmit.large-file"],
            _output,
            _error,
            parse => PreflightCommandLine.Run(parse, command))
            .ShouldBe(0);

        _output.ToString().ShouldContain("CI detected: TEAMCITY_VERSION");
    }

    /// <summary>
    /// The production composition root, exercised once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other test here injects its own environment, which is what makes
    /// them readable — and means nothing exercises the one <c>Program</c>
    /// actually builds. A composition root that named the wrong assembly or
    /// wired the wrong file system would leave all of them green. <c>graph</c>
    /// is used because it reads no policy, so the assertion does not depend on
    /// what the current directory happens to contain.
    /// </para>
    /// <para>
    /// The count rather than a containment check, since the plugin loader. This is the
    /// only test that runs against the real <c>ExecutableDirectory</c>, so it
    /// is the only one that would probe a <c>rules/</c> directory beside the
    /// test binary — and a containment check would stay green while the graph
    /// silently gained somebody's plugin. Failing loudly is the right answer to
    /// that: an assembly nobody in this repository put there has no business in
    /// this assertion.
    /// </para>
    /// </remarks>
    [Fact]
    public void Execute_ThroughTheRealEnvironment_RunsTheCommandOverTheBuiltInRulesAlone()
    {
        var output = new StringWriter();

        PreflightCommandLine.Execute(["graph"], output, new StringWriter()).ShouldBe(0);

        output.ToString().ShouldContain("core.workspace.toolchain");

        // Every line the graph indents is one rule; the rest are level headers.
        output.ToString()
            .Split('\n')
            .Count(line => line.StartsWith("  ", StringComparison.Ordinal))
            .ShouldBe(Preflight.Rules.Tests.BuiltInRuleDescriptorsTests.Discovered().Count);
    }

    /// <remarks>
    /// The overlay states <c>explain</c> can report, which <c>run</c> exercises
    /// separately. The explain command prints this line, and "not applied"
    /// alone leaves a reader unable to tell a deliberate flag from a missing
    /// file.
    /// </remarks>
    [Theory]
    [InlineData(true, false, "applied")]
    [InlineData(true, true, "--no-local")]
    [InlineData(false, false, "no local file")]
    public void Explain_ReportsWhyTheOverlayDidOrDidNotApply(
        bool writeLocal,
        bool noLocal,
        string expected)
    {
        if (writeLocal)
        {
            Write("preflight.local.json", """{ "schemaVersion": 1 }""");
        }

        string[] args = noLocal
            ? ["explain", "core.presubmit.large-file", "--no-local"]
            : ["explain", "core.presubmit.large-file"];

        Invoke(args).ShouldBe(0);

        _output.ToString().ShouldContain(expected);
    }

    /// <remarks>
    /// The target reaches the rules. <c>core.build.configuration</c> resolves
    /// its path from the platform, so a run that dropped the flag would look
    /// for the wrong file and fail about a configuration the user never named.
    /// </remarks>
    [Fact]
    public void Run_PassesThePlatformAndConfigurationToTheRules()
    {
        GivenAGoodWorkspace();
        Write("config/build/linux64.json", """{ "contentRoot": "content" }""");
        Directory.CreateDirectory(Path.Combine(_workspace.FullName, "content"));

        Invoke("run", "--stage", "build-readiness", "--platform", "linux64", "--configuration", "Shipping")
            .ShouldBe(0);

        _output.ToString().ShouldContain("linux64/Shipping");
    }

    /// <summary>
    /// <see cref="PreflightCommandLine.Run"/> refuses a command it does not
    /// know.
    /// </summary>
    /// <remarks>
    /// Unreachable through the real parser, which rejects an unknown command
    /// before dispatch — but <c>Run</c> is public and takes any parse result,
    /// and the arm exists so that a fifth command added to the surface and
    /// forgotten here fails loudly instead of reporting success having done
    /// nothing. Exercised with a parser built for the purpose, because that is
    /// the only caller shape that can reach it.
    /// </remarks>
    [Fact]
    public void Run_WithACommandItDoesNotKnow_Throws()
    {
        var foreign = new System.CommandLine.RootCommand("test");

        foreign.Add(new System.CommandLine.Command("nonsense", "not one of the four"));

        var parse = foreign.Parse(["nonsense"]);

        var environment = Injected();

        Should.Throw<RuleDiscoveryException>(() => PreflightCommandLine.Run(parse, environment))
            .Message.ShouldContain("nonsense");
    }

    /// <summary>
    /// A rule that exists only to carry a documentation URL.
    /// </summary>
    /// <remarks>
    /// In the test project rather than in <c>Preflight.Rules</c>, because the
    /// built-in rule set's admission criterion is explicit: a rule created to
    /// exercise a code path is the toy example the criterion exists to bar.
    /// </remarks>
    private sealed class DocumentedRule : IValidationRule
    {
        public RuleDescriptor Descriptor { get; } = new()
        {
            Id = new RuleId("sample.docs.rule"),
            DisplayName = "Sample",
            Stage = ValidationStage.Workspace,
            Documentation = "https://wiki.invalid/rules/sample",
        };

        public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
            Task.FromResult(RuleOutcome.Passed());
    }
}
