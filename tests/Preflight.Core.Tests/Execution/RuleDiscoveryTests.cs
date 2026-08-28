namespace Preflight.Core.Tests.Execution;

using Preflight.Abstractions.Rules;
using Preflight.Core;

/// <summary>
/// Fixes reflection discovery: which types become rules, which are ignored, and
/// which are a configuration error.
/// </summary>
/// <remarks>
/// <para>
/// the rule interface: a rule needs a public parameterless constructor,
/// and the engine instantiates it with <c>Activator.CreateInstance</c> — no
/// container involved.
/// </para>
/// <para>
/// Discovery takes the types or assemblies to scan rather than resolving them
/// itself. It has to: <c>Preflight.Core</c> must never reference
/// <c>Preflight.Rules</c>, so "the internal assembly" cannot be named from
/// inside the engine. The side effect is what makes these tests possible at
/// all — deliberately broken types can live in the test assembly.
/// </para>
/// </remarks>
public sealed class RuleDiscoveryTests
{
    [Fact]
    public void FromTypes_WithAConcretePublicRule_InstantiatesItAndReadsItsDescriptor()
    {
        var rules = RuleDiscovery.FromTypes([typeof(DiscoveryFixtures.WellFormedRule)]);

        rules.ShouldHaveSingleItem()
            .Descriptor.Id.ShouldBe(new RuleId("core.fixture.well-formed"));
    }

    /// <remarks>
    /// None of these declared an intent to be a rule that the engine could
    /// honour: an abstract class and an interface cannot be instantiated by
    /// anyone, an open generic has no closed form to construct, a non-public
    /// type was not offered, and the last one simply is not a rule.
    ///
    /// Ignoring a non-public type does not hide anything: if a policy enables
    /// its id, the policy layer's validator rejects the file with "unknown rule id" and
    /// an edit-distance suggestion. The failure surfaces, just at the layer
    /// that can explain it.
    /// </remarks>
    [Theory]
    [InlineData(typeof(DiscoveryFixtures.AbstractRule))]
    [InlineData(typeof(DiscoveryFixtures.IDerivedRuleInterface))]
    [InlineData(typeof(DiscoveryFixtures.OpenGenericRule<>))]
    [InlineData(typeof(DiscoveryFixtures.NonPublicRule))]
    [InlineData(typeof(DiscoveryFixtures.NotARule))]
    public void FromTypes_WithATypeThatIsNotACandidate_IgnoresIt(Type type)
    {
        RuleDiscovery.FromTypes([type]).ShouldBeEmpty();
    }

    /// <remarks>
    /// The contrast with the theory above, and the reason the two cannot be one
    /// rule. A public type that writes <c>: IValidationRule</c> has declared the
    /// intent; failing to give it a usable constructor is a mistake, not a
    /// decision. The load-time flow sends a load failure to exit 2 — the tool owner —
    /// while dropping it silently would leave the rule missing from a green
    /// report, which is the false green of principle 7.
    /// </remarks>
    [Fact]
    public void FromTypes_WithAPublicRuleLackingAParameterlessConstructor_ThrowsNamingTheType()
    {
        var exception = Should.Throw<RuleDiscoveryException>(
            () => RuleDiscovery.FromTypes([typeof(DiscoveryFixtures.ConstructorTakesParametersRule)]));

        exception.Message.ShouldContain(nameof(DiscoveryFixtures.ConstructorTakesParametersRule));
        exception.ShouldBeAssignableTo<ConfigurationLoadException>();
    }

    [Fact]
    public void FromTypes_WithARuleWhoseConstructorThrows_ThrowsNamingTheTypeAndTheRealCause()
    {
        var exception = Should.Throw<RuleDiscoveryException>(
            () => RuleDiscovery.FromTypes([typeof(DiscoveryFixtures.ThrowingConstructorRule)]));

        exception.Message.ShouldContain(nameof(DiscoveryFixtures.ThrowingConstructorRule));
        exception.Message.ShouldContain("refuses to be constructed");
    }

    /// <remarks>
    /// Discovery does not adjudicate duplicates: it returns both, and
    /// <c>RuleGraph.Build</c> reports <c>DuplicateRuleId</c>, which it already
    /// knows how to do. Deciding here by load order would make the outcome
    /// depend on filesystem enumeration, which plugin loading calls the
    /// definition of non-deterministic.
    /// </remarks>
    [Fact]
    public void FromTypes_WithTwoRulesSharingARuleId_ReturnsBothAndLetsTheGraphReportIt()
    {
        var rules = RuleDiscovery.FromTypes([
            typeof(DiscoveryFixtures.DuplicateIdRuleA),
            typeof(DiscoveryFixtures.DuplicateIdRuleB),
        ]);

        rules.Count.ShouldBe(2);

        var exception = Should.Throw<GraphValidationException>(
            () => RuleGraph.Build([.. rules.Select(rule => rule.Descriptor)]));

        exception.Errors.ShouldContain(error => error is GraphValidationError.DuplicateRuleId);
    }

    /// <remarks>
    /// <c>Assembly.GetTypes</c> gives no ordering guarantee between builds, so
    /// without an explicit sort the whole run stops being reproducible at its
    /// very first step. The input here is shuffled deliberately rather than
    /// left to reflection's luck.
    /// </remarks>
    [Fact]
    public void FromTypes_WithTypesInAnyOrder_ReturnsThemInRuleIdOrdinalOrder()
    {
        var rules = RuleDiscovery.FromTypes([
            typeof(DiscoveryFixtures.WellFormedRule),
            typeof(DiscoveryFixtures.SecondWellFormedRule),
        ]);

        rules.Select(rule => rule.Descriptor.Id.Value)
            .ShouldBe(["core.fixture.second", "core.fixture.well-formed"]);
    }

    [Fact]
    public void FromTypes_WithNoTypes_ReturnsEmpty()
    {
        RuleDiscovery.FromTypes([]).ShouldBeEmpty();
    }

    /// <remarks>
    /// <para>
    /// Scanning a whole assembly is the shape the CLI uses, and this assembly
    /// deliberately contains rules that cannot be discovered. The result is the
    /// loud failure the load-time flow asks for: one broken rule stops the load rather
    /// than quietly shrinking the rule set.
    /// </para>
    /// <para>
    /// The assertion is that a type is named by its full name, not that a
    /// particular one is. It used to name
    /// <c>ConstructorTakesParametersRule</c>, which worked only because that
    /// was the assembly's single broken fixture; the plugin loader added two more — a
    /// computed descriptor that throws, and an id <see cref="RuleId"/> rejects —
    /// and which of the three the scan reaches first is
    /// <see cref="System.Reflection.Assembly.GetTypes"/>'s business, about which
    /// it promises nothing. Pinning one of them would be pinning an
    /// implementation detail of the runtime.
    /// </para>
    /// <para>
    /// The type name rather than the rule id, in every one of those cases: the
    /// engine cannot read a descriptor off a rule it could not build, so the
    /// type is all it knows to name — and it is what the person fixing it needs
    /// anyway.
    /// </para>
    /// </remarks>
    [Fact]
    public void FromAssemblies_WithAnAssemblyContainingABrokenRule_FailsNamingIt()
    {
        var exception = Should.Throw<RuleDiscoveryException>(
            () => RuleDiscovery.FromAssemblies(typeof(DiscoveryFixtures).Assembly));

        exception.Message.ShouldContain(typeof(DiscoveryFixtures).Assembly.GetName().Name!);
    }

    /// <remarks>
    /// Containment, never set equality: the test assembly gains types whenever
    /// anyone adds a fixture, and an equality assertion would turn that into an
    /// unrelated failure.
    /// </remarks>
    [Fact]
    public void FromAssemblies_WithAnAssembly_FindsItsWellFormedRulesAndSkipsTheRest()
    {
        var rules = RuleDiscovery.FromTypes(
            typeof(DiscoveryFixtures.WellFormedRule).Assembly.GetTypes()
                .Where(type => type.Namespace == typeof(DiscoveryFixtures).Namespace)
                .Where(type => type != typeof(DiscoveryFixtures.ConstructorTakesParametersRule))
                .Where(type => type != typeof(DiscoveryFixtures.ThrowingConstructorRule))
                .ToArray());

        var ids = rules.Select(rule => rule.Descriptor.Id.Value).ToArray();

        ids.ShouldContain("core.fixture.well-formed");
        ids.ShouldNotContain("core.fixture.abstract");
        ids.ShouldNotContain("core.fixture.generic");
        ids.ShouldNotContain("core.fixture.non-public");
    }
}
