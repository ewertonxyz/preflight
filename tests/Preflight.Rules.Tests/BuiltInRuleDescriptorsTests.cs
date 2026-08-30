namespace Preflight.Rules.Tests;

using System.Reflection;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Rules;

/// <summary>
/// Fixes the built-in rule table and the graph it produces.
/// </summary>
/// <remarks>
/// <c>RuleDescriptor</c> defaults both <c>DefaultBlocking</c> and
/// <c>DefaultGating</c> to <see langword="true"/>, and most of the built-in
/// rules are leaves that need <c>gating: false</c>. A forgotten override is
/// invisible: it compiles, the rule behaves identically on its own, and the
/// only symptom is that a failure now skips rules it should not have — which
/// reads as the graph being wrong rather than as one missing line in one
/// descriptor.
/// </remarks>
public sealed class BuiltInRuleDescriptorsTests
{
    /// <summary>
    /// Every rule in <c>Preflight.Rules</c>, found the way the engine finds
    /// them.
    /// </summary>
    /// <remarks>
    /// By reflection over the assembly rather than a hand-written list, so a
    /// rule added without a row in the table below fails the count instead of
    /// quietly joining the set. A hand-written list would have to be edited to
    /// notice the new rule, which is exactly the edit whoever forgot the row
    /// also forgot.
    /// </remarks>
    public static IReadOnlyList<IValidationRule> Discovered() =>
    [
        .. typeof(BuiltInRuleIds).Assembly
            .GetTypes()
            .Where(type => typeof(IValidationRule).IsAssignableFrom(type) && !type.IsAbstract && type.IsVisible)
            .Select(type => (IValidationRule)Activator.CreateInstance(type)!)
            .OrderBy(rule => rule.Descriptor.Id.Value, StringComparer.Ordinal),
    ];

    private static RuleDescriptor DescriptorOf(RuleId id) =>
        Discovered().Single(rule => rule.Descriptor.Id == id).Descriptor;

    [Fact]
    public void Discovery_FindsExactlyTheBuiltInRuleSet()
    {
        Discovered().Select(rule => rule.Descriptor.Id.Value).ShouldBe([
            "core.build.compile-probe",
            "core.build.configuration",
            "core.presubmit.forbidden-paths",
            "core.presubmit.large-file",
            "core.workspace.dependencies",
            "core.workspace.toolchain",
        ]);
    }

    /// <summary>
    /// Stage, dependency, blocking and gating, rule by rule.
    /// </summary>
    /// <remarks>
    /// Gating is asserted on every rule, including the leaves where it changes
    /// nothing. A value that is merely inherited is a value nobody decided, and
    /// the next person to add a rule copies whichever one they read first.
    /// </remarks>
    [Theory]
    [InlineData("core.workspace.toolchain", ValidationStage.Workspace, "", true, true)]
    [InlineData("core.workspace.dependencies", ValidationStage.Workspace, "core.workspace.toolchain", true, false)]
    [InlineData("core.presubmit.forbidden-paths", ValidationStage.PreSubmit, "", true, false)]
    [InlineData("core.presubmit.large-file", ValidationStage.PreSubmit, "", true, false)]
    [InlineData("core.build.configuration", ValidationStage.BuildReadiness, "core.workspace.toolchain", true, true)]
    [InlineData("core.build.compile-probe", ValidationStage.BuildReadiness, "core.build.configuration", true, false)]
    public void Descriptor_DeclaresItsStageDependencyBlockingAndGating(
        string id,
        ValidationStage stage,
        string dependsOn,
        bool blocking,
        bool gating)
    {
        var descriptor = DescriptorOf(new RuleId(id));

        descriptor.Stage.ShouldBe(stage);
        descriptor.DefaultBlocking.ShouldBe(blocking);
        descriptor.DefaultGating.ShouldBe(gating);

        descriptor.DependsOn.Select(dependency => dependency.Value)
            .ShouldBe(dependsOn.Length == 0 ? [] : [dependsOn]);
    }

    [Fact]
    public void Descriptor_GivesEveryRuleADisplayName()
    {
        Discovered().ShouldAllBe(rule => !string.IsNullOrWhiteSpace(rule.Descriptor.DisplayName));
    }

    /// <summary>
    /// The shape the descriptors add up to.
    /// </summary>
    /// <remarks>
    /// Two independent roots at pre-submit, and a chain three deep ending at
    /// the expensive rule. Asserted separately from the table above because the
    /// two are one fact stated twice, and either can be edited without the
    /// other: a dependency moved one row up still produces a table that reads
    /// fine and a graph that no longer defers the compile.
    /// </remarks>
    [Fact]
    public void Descriptors_ProduceTwoPreSubmitRootsAndAChainEndingAtTheProbe()
    {
        var descriptors = Discovered().Select(rule => rule.Descriptor).ToArray();

        var roots = descriptors
            .Where(descriptor => descriptor.DependsOn.Count == 0)
            .Select(descriptor => descriptor.Id.Value)
            .Order(StringComparer.Ordinal);

        roots.ShouldBe([
            "core.presubmit.forbidden-paths",
            "core.presubmit.large-file",
            "core.workspace.toolchain",
        ]);

        DescriptorOf(BuiltInRuleIds.CompileProbe).DependsOn.ShouldBe([BuiltInRuleIds.BuildConfiguration]);
        DescriptorOf(BuiltInRuleIds.BuildConfiguration).DependsOn.ShouldBe([BuiltInRuleIds.Toolchain]);
    }

    /// <summary>
    /// Every dependency names a rule that exists.
    /// </summary>
    /// <remarks>
    /// A typo in a <c>DependsOn</c> id is not a compile error. It surfaces at
    /// graph-build time as "no rule with that id", which is hard to tell apart
    /// from a rule the policy disabled — so the reader goes looking at the
    /// policy, finds nothing wrong with it, and never opens the descriptor
    /// where the typo actually is.
    /// </remarks>
    [Fact]
    public void Descriptors_DependOnlyOnRulesThatExist()
    {
        var known = Discovered().Select(rule => rule.Descriptor.Id).ToHashSet();

        foreach (var descriptor in Discovered().Select(rule => rule.Descriptor))
        {
            descriptor.DependsOn.ShouldAllBe(dependency => known.Contains(dependency));
        }
    }

    /// <remarks>
    /// A rule needs a public parameterless constructor, because the engine
    /// instantiates it with <see cref="Activator"/> and no container is
    /// involved. A rule that grew a constructor parameter would be found by
    /// discovery and then fail to be created, mid-run.
    /// </remarks>
    [Fact]
    public void EveryRule_HasAPublicParameterlessConstructor()
    {
        foreach (var rule in Discovered())
        {
            rule.GetType()
                .GetConstructor(BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)
                .ShouldNotBeNull($"{rule.GetType().Name} must be constructible by the engine.");
        }
    }
}
