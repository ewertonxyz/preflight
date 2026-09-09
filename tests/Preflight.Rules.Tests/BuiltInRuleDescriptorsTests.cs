namespace Preflight.Rules.Tests;

using System.Reflection;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core.Execution;
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
    /// Every rule in <c>Preflight.Rules</c>, found the way the tool finds
    /// them.
    /// </summary>
    /// <remarks>
    /// The product's own discovery, called rather than reimplemented. A copy of
    /// the walk written here asserts the set as the <em>test</em> computes it,
    /// which is a different set the moment the two disagree about a filter —
    /// and the copy that used to live here did disagree, silently.
    /// </remarks>
    public static IReadOnlyList<IValidationRule> Discovered() =>
    [
        .. RuleDiscovery.FromAssemblies(typeof(BuiltInRuleIds).Assembly)
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
            "core.build.platform-sdk",
            "core.presubmit.companion-file",
            "core.presubmit.forbidden-paths",
            "core.presubmit.large-file",
            "core.presubmit.lfs-pointer",
            "core.presubmit.line-endings",
            "core.presubmit.merge-artifact",
            "core.presubmit.mutable-reference",
            "core.presubmit.path-portability",
            "core.workspace.approved-dependencies",
            "core.workspace.dependencies",
            "core.workspace.environment",
            "core.workspace.free-space",
            "core.workspace.submodule-pin",
            "core.workspace.toolchain",
            "core.workspace.vcs-configuration",
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
    [InlineData("core.presubmit.lfs-pointer", ValidationStage.PreSubmit, "", true, false)]
    [InlineData("core.presubmit.path-portability", ValidationStage.PreSubmit, "", true, false)]
    [InlineData("core.presubmit.companion-file", ValidationStage.PreSubmit, "", true, false)]
    [InlineData("core.workspace.free-space", ValidationStage.Workspace, "core.workspace.toolchain", true, false)]
    [InlineData(
        "core.workspace.approved-dependencies",
        ValidationStage.Workspace,
        "core.workspace.toolchain",
        true,
        false)]
    [InlineData("core.build.platform-sdk", ValidationStage.BuildReadiness, "core.workspace.toolchain", true, false)]
    [InlineData(
        "core.workspace.vcs-configuration",
        ValidationStage.Workspace,
        "core.workspace.toolchain",
        true,
        false)]
    [InlineData("core.workspace.environment", ValidationStage.Workspace, "", true, false)]
    [InlineData("core.workspace.submodule-pin", ValidationStage.Workspace, "core.workspace.toolchain", true, false)]
    [InlineData("core.presubmit.line-endings", ValidationStage.PreSubmit, "", true, false)]
    [InlineData("core.presubmit.mutable-reference", ValidationStage.PreSubmit, "", true, false)]
    [InlineData("core.presubmit.merge-artifact", ValidationStage.PreSubmit, "", true, false)]
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
    /// Every rule has a row in the table above.
    /// </summary>
    /// <remarks>
    /// Without this, a rule added without a row is a rule whose stage,
    /// dependency, blocking and gating nobody ever asserts — and the way that
    /// arrives is a set growing by six at once, where one omission reads as
    /// five rows having been added carefully.
    /// </remarks>
    [Fact]
    public void EveryDiscoveredRule_HasARowInTheDescriptorTable()
    {
        var rows = typeof(BuiltInRuleDescriptorsTests)
            .GetMethod(nameof(Descriptor_DeclaresItsStageDependencyBlockingAndGating))!
            .GetCustomAttributes<InlineDataAttribute>()
            .Select(row => (string)row.Data[0]!)
            .Order(StringComparer.Ordinal);

        rows.ShouldBe(
            Discovered().Select(rule => rule.Descriptor.Id.Value).Order(StringComparer.Ordinal),
            "A descriptor with no row has nothing asserted about it at all.");
    }

    /// <summary>
    /// The shape the descriptors add up to.
    /// </summary>
    /// <remarks>
    /// Every independent root, and a chain three deep ending at the expensive
    /// rule. Asserted separately from the table above because the two are one
    /// fact stated twice, and either can be edited without the other: a
    /// dependency moved one row up still produces a table that reads fine and a
    /// graph that no longer defers the compile.
    ///
    /// The roots are not all pre-submit, which the old name of this test
    /// claimed and the list never did: the toolchain rule has always been one,
    /// and the environment rule is the second from its stage.
    /// </remarks>
    [Fact]
    public void Descriptors_ProduceTheIndependentRootsAndAChainEndingAtTheProbe()
    {
        var descriptors = Discovered().Select(rule => rule.Descriptor).ToArray();

        var roots = descriptors
            .Where(descriptor => descriptor.DependsOn.Count == 0)
            .Select(descriptor => descriptor.Id.Value)
            .Order(StringComparer.Ordinal);

        roots.ShouldBe([
            "core.presubmit.companion-file",
            "core.presubmit.forbidden-paths",
            "core.presubmit.large-file",
            "core.presubmit.lfs-pointer",
            "core.presubmit.line-endings",
            "core.presubmit.merge-artifact",
            "core.presubmit.mutable-reference",
            "core.presubmit.path-portability",
            "core.workspace.environment",
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
    /// A rule needs a public parameterless constructor, because the tool
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
                .ShouldNotBeNull($"{rule.GetType().Name} must be constructible by the tool.");
        }
    }
}
