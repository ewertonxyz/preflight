namespace Preflight.Cli.Tests.Plugins;

using Preflight.Cli.Commands;
using Preflight.Cli.Tests.Commands;
using Preflight.TestSupport;

/// <summary>
/// The commands of the command surface with a real plugin on a real disk.
/// </summary>
/// <remarks>
/// <para>
/// What the loader's own tests cannot show: whether the pieces were connected
/// at all. A loader that works perfectly and is never called from
/// <c>rules</c>, <c>graph</c> or <c>explain</c> leaves every test in
/// <c>Preflight.Core.Tests</c> green and every policy naming a plugin rule
/// rejected with "unknown rule id".
/// </para>
/// <para>
/// The environment is injected rather than the process spawned, so standard
/// error is readable. <c>Preflight.Specs</c> spawns the real binary for the
/// contract a process has and an in-process call cannot observe.
/// </para>
/// </remarks>
public sealed class PluginCommandTests : IDisposable
{
    private readonly DirectoryInfo _workspace = Directory.CreateTempSubdirectory("preflight-plugin-cli-");
    private readonly DirectoryInfo _plugins = PluginFixtures.PluginDirectory();
    private readonly DirectoryInfo _executable = Directory.CreateTempSubdirectory("preflight-plugin-bin-");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public void Dispose()
    {
        PluginFixtures.TryDelete(_workspace);
        PluginFixtures.TryDelete(_plugins);
        PluginFixtures.TryDelete(_executable);
        _output.Dispose();
        _error.Dispose();
    }

    /// <remarks>
    /// The plugin rule appears beside the six built-ins rather than instead of
    /// them, and it is marked with its stage and level like any other. Nothing
    /// in the renderer may assume a <c>core.</c> prefix.
    /// </remarks>
    [Fact]
    public void Rules_WithAPluginPath_ListsThePluginRuleAmongTheBuiltIns()
    {
        Invoke("rules", "--rules-path", _plugins.FullName).ShouldBe(0);

        _output.ToString().ShouldContain(PluginFixtures.SampleRuleId);
        _output.ToString().ShouldContain("core.presubmit.large-file");
    }

    [Fact]
    public void Graph_WithAPluginPath_PlacesThePluginRuleAtALevel()
    {
        Invoke("graph", "--rules-path", _plugins.FullName).ShouldBe(0);

        _output.ToString().ShouldContain(PluginFixtures.SampleRuleId);
    }

    /// <summary>
    /// <c>explain</c> answers about a plugin rule.
    /// </summary>
    /// <remarks>
    /// Policy precedence says the point of the command is that "why is this limit
    /// 4096?" has an answer in one command rather than in an archaeology of JSON
    /// files. A plugin rule is exactly the case where the archaeology would be
    /// hardest, because the reader does not own the rule either.
    /// </remarks>
    [Fact]
    public void Explain_WithAPluginRuleId_ResolvesIt()
    {
        Invoke("explain", PluginFixtures.SampleRuleId, "--rules-path", _plugins.FullName).ShouldBe(0);

        _output.ToString().ShouldContain("Texture dimension");
    }

    /// <remarks>
    /// The control. Without it, the test above would pass against a command that
    /// accepts any id at all.
    /// </remarks>
    [Fact]
    public void Explain_WithoutThePluginPath_DoesNotKnowThatRuleId()
    {
        Invoke("explain", PluginFixtures.SampleRuleId).ShouldBe(2);

        _error.ToString().ShouldContain("No rule with id");
    }

    /// <summary>
    /// A policy naming a plugin's rule is a valid policy.
    /// </summary>
    /// <remarks>
    /// The reason the load-time flow loads plugins before it validates policy, seen
    /// from the side where it works. Six of the seven commands resolve a policy,
    /// and every one of them validates its rule keys against the discovered
    /// descriptors.
    /// </remarks>
    [Fact]
    public void Rules_WithAPolicyNamingThePluginRule_AcceptsIt()
    {
        GivenPolicyNamingThePluginRule();

        Invoke("rules", "--rules-path", _plugins.FullName).ShouldBe(0);
    }

    /// <remarks>
    /// And the same policy without the plugin is the "unknown rule id" this
    /// ordering exists to keep out of the way of real load failures. Both halves
    /// are needed: the first proves the plugin is seen, this one proves policy
    /// validation is still doing its job.
    /// </remarks>
    [Fact]
    public void Rules_WithAPolicyNamingAPluginThatWasNotLoaded_ReportsAnUnknownRuleId()
    {
        GivenPolicyNamingThePluginRule();

        Invoke("rules").ShouldBe(2);

        _error.ToString().ShouldContain("unknown rule id");
    }

    /// <summary>
    /// A broken plugin is reported as a broken plugin, and never as a typo.
    /// </summary>
    /// <remarks>
    /// The load ordering, and the only way it is observable: what the user is
    /// told. If the policy were validated first, the keys belonging to the
    /// plugin that did not load would read as "unknown rule id" — sending
    /// somebody to hunt for a typo in a correct policy file while the DLL that
    /// would not open goes unmentioned.
    ///
    /// The assertion of <em>absence</em> is the point of the test. Naming the
    /// DLL is easy to get right by accident; not also naming the rule is what
    /// the ordering buys.
    /// </remarks>
    [Fact]
    public void Rules_WithABrokenPluginAndAPolicyNamingItsRule_NamesTheDllAndNotTheRuleId()
    {
        var broken = PluginFixtures.BrokenPluginDirectory();

        try
        {
            GivenPolicyNamingThePluginRule();

            Invoke("rules", "--rules-path", broken.FullName).ShouldBe(2);

            _error.ToString().ShouldContain("Broken.Rules.dll");
            _error.ToString().ShouldNotContain("unknown rule id");
            _error.ToString().ShouldNotContain("Did you mean");
        }
        finally
        {
            PluginFixtures.TryDelete(broken);
        }
    }

    /// <summary>
    /// A <c>rules/</c> directory beside the executable is probed without a flag.
    /// </summary>
    /// <remarks>
    /// Plugin loading's other source. It resolves against the executable and never
    /// against the workspace, which is why the environment carries the directory
    /// rather than reading it where it is used: a workspace is frequently a
    /// checkout the person running <c>preflight</c> did not write, and probing
    /// <c>rules/</c> inside it would execute code committed to the repository
    /// under validation.
    /// </remarks>
    [Fact]
    public void Rules_WithAPluginBesideTheExecutable_FindsItWithoutAFlag()
    {
        GivenAPluginBesideTheExecutable();

        Invoke("rules").ShouldBe(0);

        _output.ToString().ShouldContain(PluginFixtures.SampleRuleId);
    }

    /// <remarks>
    /// The counterweight: a <c>rules/</c> directory planted in the workspace is
    /// not probed. Without this, the test above would pass against an
    /// implementation that resolved either way.
    /// </remarks>
    [Fact]
    public void Rules_WithAPluginDirectoryPlantedInTheWorkspace_IgnoresIt()
    {
        CopyPluginsInto(Path.Combine(_workspace.FullName, "rules"));

        Invoke("rules").ShouldBe(0);

        _output.ToString().ShouldNotContain(PluginFixtures.SampleRuleId);
    }

    /// <summary>
    /// The same plugin deployed twice is a refusal naming both copies.
    /// </summary>
    /// <remarks>
    /// The collision that actually happens, and the reason an assembly is
    /// identified by its path: both copies carry the same assembly name, so a
    /// message built from names would report one claimant and hide the
    /// duplication entirely.
    /// </remarks>
    [Fact]
    public void Rules_WithTheSamePluginInTwoDirectories_IsTwoNamingBothCopies()
    {
        var second = PluginFixtures.PluginDirectory();

        try
        {
            Invoke("rules", "--rules-path", _plugins.FullName, "--rules-path", second.FullName)
                .ShouldBe(2);

            _error.ToString().ShouldContain(_plugins.FullName);
            _error.ToString().ShouldContain(second.FullName);
            _error.ToString().ShouldContain(PluginFixtures.SampleRuleId);
        }
        finally
        {
            PluginFixtures.TryDelete(second);
        }
    }

    /// <remarks>
    /// A directory given twice is not a collision with itself. The plausible
    /// mistake is pointing <c>--rules-path</c> at the directory that would have
    /// been probed anyway, and a tool that refused over it would be rejecting a
    /// correct configuration.
    /// </remarks>
    [Fact]
    public void Rules_WithOneDirectoryGivenAndAlsoImplicit_IsNotACollision()
    {
        GivenAPluginBesideTheExecutable();

        Invoke("rules", "--rules-path", Path.Combine(_executable.FullName, "rules")).ShouldBe(0);

        _output.ToString().ShouldContain(PluginFixtures.SampleRuleId);
    }

    private void GivenPolicyNamingThePluginRule() => File.WriteAllText(
        Path.Combine(_workspace.FullName, "preflight.base.json"),
        $$"""
        {
          "schemaVersion": 1,
          "rules": { "{{PluginFixtures.SampleRuleId}}": { "settings": { "maxDimension": 2048 } } }
        }
        """);

    private void GivenAPluginBesideTheExecutable() =>
        CopyPluginsInto(Path.Combine(_executable.FullName, "rules"));

    private void CopyPluginsInto(string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in _plugins.EnumerateFiles())
        {
            file.CopyTo(Path.Combine(destination, file.Name), overwrite: true);
        }
    }

    private int Invoke(params string[] args) => PreflightCommandLine.Execute(
        args,
        _output,
        _error,
        parse => CommandDispatcher.Run(parse, Injected()));

    private CommandEnvironment Injected() => CommandEnvironments.For(
        _workspace,
        _output,
        _error,
        TimeProvider.System,
        executableDirectory: _executable);
}
