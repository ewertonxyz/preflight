namespace Preflight.Core.Tests.Plugins;

using Preflight.Abstractions.Rules;
using Preflight.Core.Plugins;
using Preflight.TestSupport;

/// <summary>
/// What plugin loading decides, one decision at a time.
/// </summary>
/// <remarks>
/// Driven through <see cref="FakeAssemblyLoader"/>, so every case is a
/// statement about an assembly rather than an assembly. The one thing that
/// cannot be faked — whether a real load context hands back the host's contract
/// — is asserted against real files in <c>Preflight.Cli.Tests</c>, which is the
/// only place both halves of that question exist.
/// </remarks>
public sealed class PluginLoaderTests
{
    private static readonly Version Host = new(1, 2, 0);

    /// <summary>
    /// A run with no plugins gets its own list back.
    /// </summary>
    /// <remarks>
    /// The overwhelmingly common case, and it should not pay for a copy, a sort
    /// and a grouping to arrive back where it started. Reference equality is
    /// the assertion because value equality would pass over a copy.
    /// </remarks>
    [Fact]
    public void Load_WithNothingToProbe_ReturnsTheBuiltInRulesThemselves()
    {
        IReadOnlyList<IValidationRule> builtIn = [new SampleRule()];

        Load(builtIn, Probe()).ShouldBeSameAs(builtIn);
    }

    [Fact]
    public void Load_WithAPluginRule_AddsItToTheBuiltInSet() =>
        Load([new AnotherSampleRule()], Probe("a.dll"), Assembly("a.dll", typeof(SampleRule)))
            .Select(rule => rule.Descriptor.Id.Value)
            .ShouldBe([AnotherSampleRule.Id, SampleRule.Id]);

    /// <summary>
    /// An assembly that does not reference the contract is not a plugin, and
    /// not a problem.
    /// </summary>
    /// <remarks>
    /// The counterweight to every refusal in this file. A rules directory
    /// legitimately holds the helper libraries a rule depends on, and a loader
    /// that refused to start because one of them declares no rules would make
    /// the directory unusable for the thing it exists for.
    /// </remarks>
    [Fact]
    public void Load_WithAnAssemblyThatDoesNotReferenceTheContract_SkipsItWithoutFailing()
    {
        IReadOnlyList<IValidationRule> builtIn = [new AnotherSampleRule()];

        var assembly = Assembly("helper.dll", typeof(HelperType)) with { AbstractionsReference = null };

        Load(builtIn, Probe("helper.dll"), assembly).ShouldBeSameAs(builtIn);
    }

    /// <remarks>
    /// The plugin asks for a minor the host does not have — the plugin version
    /// contract's third row, the asymmetric one. The message has to name both
    /// numbers, because which side is behind is the only thing that decides
    /// whether the reader rebuilds a plugin or upgrades the tool.
    /// </remarks>
    [Fact]
    public void Load_WithAnIncompatibleContract_RefusesNamingBothVersions()
    {
        var assembly = Assembly("a.dll", typeof(SampleRule)) with
        {
            AbstractionsReference = new Version(1, 4, 0),
        };

        var message = Refusal([], Probe("a.dll"), assembly);

        message.ShouldContain("1.4.0");
        message.ShouldContain("1.2.0");
    }

    /// <remarks>
    /// The rule interface, and the assembly is named as well as the type.
    /// <c>RuleDiscovery</c> names only the type, which is enough when the types
    /// came from the engine's own assembly and is not enough when a directory
    /// held four plugins.
    /// </remarks>
    [Fact]
    public void Load_WithARuleTypeThatNeedsAConstructorArgument_NamesTheAssemblyAndTheType()
    {
        var message = Refusal([], Probe("a.dll"), Assembly("a.dll", typeof(ConstructorArgumentRule)));

        message.ShouldContain("a.dll");
        message.ShouldContain(nameof(ConstructorArgumentRule));
    }

    /// <summary>
    /// A descriptor that throws when it is read is exit 2, not exit 3.
    /// </summary>
    /// <remarks>
    /// The defect this test was written for lived in <c>RuleDiscovery</c>,
    /// which read <c>rule.Descriptor.Id.Value</c> inside the ordering step,
    /// outside every <c>try</c> in the file. A rule with a computed descriptor
    /// therefore left the process on the runtime's own exit code with a stack
    /// trace — the exit-code contract calls that an internal error, which
    /// routes the incident to the tool's owner instead of to the person whose
    /// rule is broken.
    /// </remarks>
    [Fact]
    public void Load_WithARuleWhoseDescriptorThrowsWhenRead_IsAConfigurationErrorNamingTheType()
    {
        var exception = Should.Throw<PluginLoadException>(() =>
            Load([], Probe("a.dll"), Assembly("a.dll", typeof(ThrowingDescriptorRule))));

        exception.ShouldBeAssignableTo<ConfigurationLoadException>();
        exception.Message.ShouldContain(nameof(ThrowingDescriptorRule));
        exception.Message.ShouldContain("no descriptor for you");
    }

    /// <remarks>
    /// A separate path from the one above: the id is built in a field
    /// initialiser, so <see cref="RuleId"/> throws while the rule is being
    /// constructed rather than while its descriptor is read.
    /// </remarks>
    [Fact]
    public void Load_WithADescriptorCarryingAnInvalidRuleId_NamesTheType() =>
        Refusal([], Probe("a.dll"), Assembly("a.dll", typeof(InvalidRuleIdRule)))
            .ShouldContain(nameof(InvalidRuleIdRule));

    /// <summary>
    /// Two assemblies claiming one id produce the same message in either order.
    /// </summary>
    /// <remarks>
    /// Plugin loading refuses to pick one of the two by load order, because
    /// that would make the result depend on the file system's enumeration order
    /// — the definition of non-deterministic. Byte equality across the two
    /// orderings is the assertion, rather than "both names appear": the latter
    /// passes over a message that lists them in whatever order they arrived.
    ///
    /// The two are named by path. The case that actually happens is one plugin
    /// deployed into two directories, where a simple assembly name is the same
    /// for both and would report a single claimant.
    /// </remarks>
    [Fact]
    public void Load_WithTwoAssembliesClaimingOneRuleId_ProducesTheSameMessageInEitherOrder()
    {
        var first = Assembly("acme.dll", typeof(SampleRule));
        var second = Assembly("zeta.dll", typeof(CollidingRule));

        var forwards = Refusal([], Probe("acme.dll", "zeta.dll"), first, second);
        var backwards = Refusal([], Probe("zeta.dll", "acme.dll"), second, first);

        forwards.ShouldBe(backwards);
        forwards.ShouldContain("acme.dll");
        forwards.ShouldContain("zeta.dll");
        forwards.ShouldContain(SampleRule.Id);
    }

    /// <summary>
    /// A plugin claiming a built-in id is the same defect, and the built-in
    /// does not win.
    /// </summary>
    /// <remarks>
    /// The built-in rules and an external plugin are the same kind of citizen.
    /// Preferring the built-in would be the load-order rule wearing a different
    /// hat, and it would leave the plugin author staring at a rule that is on
    /// disk, enabled by policy, and never runs.
    /// </remarks>
    [Fact]
    public void Load_WithAPluginClaimingABuiltInRuleId_NamesBothAssemblies()
    {
        var message = Refusal([new SampleRule()], Probe("a.dll"), Assembly("a.dll", typeof(CollidingRule)));

        message.ShouldContain("a.dll");
        message.ShouldContain(typeof(SampleRule).Assembly.GetName().Name!);
    }

    [Fact]
    public void Load_WithAnAssemblyThatWillNotOpen_ReportsItNamingTheFile()
    {
        var loader = new FakeAssemblyLoader().Failing("broken.dll", "not a managed assembly");

        Should.Throw<PluginLoadException>(() => new PluginLoader(loader, Host).Load([], Probe("broken.dll")))
            .Message.ShouldContain("broken.dll");
    }

    /// <summary>
    /// One broken assembly among several contributes nothing at all.
    /// </summary>
    /// <remarks>
    /// "Warn and carry on" is rejected in every one of its forms, and this is
    /// the form that looks most reasonable: three plugins loaded, one did not,
    /// run the three. A run that finishes without rules the policy declared
    /// enabled and reports success is the false green of principle 7 — worse
    /// than a noisy failure, because nobody investigates a green.
    /// </remarks>
    [Fact]
    public void Load_WithOneBrokenAssemblyAmongSeveral_ContributesNoRuleAtAll()
    {
        var loader = new FakeAssemblyLoader()
            .Containing(Assembly("good.dll", typeof(SampleRule)))
            .Failing("broken.dll", "not a managed assembly");

        Should.Throw<PluginLoadException>(() =>
            new PluginLoader(loader, Host).Load([], Probe("good.dll", "broken.dll")));
    }

    /// <remarks>
    /// Accumulated, matching policy and graph validation. Somebody who pointed
    /// <c>--rules-path</c> at a directory of plugins built against last
    /// quarter's contract should be told about all of them, not asked to run
    /// the tool once per plugin.
    /// </remarks>
    [Fact]
    public void Load_WithSeveralProblems_ReportsEveryOne()
    {
        var stale = Assembly("stale.dll", typeof(SampleRule)) with
        {
            AbstractionsReference = new Version(9, 0, 0),
        };

        var loader = new FakeAssemblyLoader()
            .Containing(stale)
            .Containing(Assembly("bad.dll", typeof(ConstructorArgumentRule)))
            .Failing("broken.dll", "not a managed assembly");

        Should.Throw<PluginLoadException>(() =>
                new PluginLoader(loader, Host).Load([], Probe("stale.dll", "bad.dll", "broken.dll")))
            .Errors.Count.ShouldBe(3);
    }

    /// <remarks>
    /// The errors a path resolution already found travel into the same refusal
    /// rather than being raised separately, so a mistyped path and a stale
    /// plugin are reported together.
    /// </remarks>
    [Fact]
    public void Load_WithAnErrorFromPathResolution_CarriesItIntoTheRefusal()
    {
        var probe = new PluginProbeResult(
            [],
            [new PluginLoadError.PluginPathUnusable("/nowhere", "does not exist")]);

        Should.Throw<PluginLoadException>(() => new PluginLoader(new FakeAssemblyLoader(), Host).Load([], probe))
            .Message.ShouldContain("/nowhere");
    }

    /// <remarks>
    /// Ordinally, and across assemblies rather than within each one. Two
    /// directories probed in either order have to produce the same list,
    /// because the graph and every reporter build their own order on top of
    /// this one and the determinism guarantee asks the whole report to be
    /// byte-identical between runs.
    /// </remarks>
    [Fact]
    public void Load_OrdersPluginRulesOrdinallyWhicheverOrderTheyWereProbedIn()
    {
        var one = Assembly("z.dll", typeof(SampleRule));
        var another = Assembly("a.dll", typeof(AnotherSampleRule));

        Load([], Probe("z.dll", "a.dll"), one, another)
            .Select(rule => rule.Descriptor.Id.Value)
            .ShouldBe([AnotherSampleRule.Id, SampleRule.Id]);
    }

    private static PluginProbeResult Probe(params string[] paths) => new(paths, []);

    private static LoadedPluginAssembly Assembly(string path, params Type[] types) => new()
    {
        Path = path,
        AbstractionsReference = new Version(1, 2, 0),
        Types = types,
    };

    private static IReadOnlyList<IValidationRule> Load(
        IReadOnlyList<IValidationRule> builtInRules,
        PluginProbeResult probe,
        params LoadedPluginAssembly[] assemblies)
    {
        var loader = new FakeAssemblyLoader();

        foreach (var assembly in assemblies)
        {
            loader.Containing(assembly);
        }

        return new PluginLoader(loader, Host).Load(builtInRules, probe);
    }

    private static string Refusal(
        IReadOnlyList<IValidationRule> builtInRules,
        PluginProbeResult probe,
        params LoadedPluginAssembly[] assemblies) =>
        Should.Throw<PluginLoadException>(() => Load(builtInRules, probe, assemblies)).Message;
}
