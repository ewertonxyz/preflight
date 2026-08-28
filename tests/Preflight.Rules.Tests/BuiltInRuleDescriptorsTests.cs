namespace Preflight.Rules.Tests;

using System.Reflection;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Rules;

/// <summary>
/// Fixes the built-in rule table and the graph it produces.
/// </summary>
/// <remarks>
/// <para>
/// <c>RuleDescriptor</c> defaults both <c>DefaultBlocking</c> and
/// <c>DefaultGating</c> to <see langword="true"/>, and the rule set requires
/// <c>gating: false</c> on four of the six. A forgotten override is invisible:
/// it compiles, the rule behaves identically on its own, and the only symptom
/// is that a failure now skips rules it should not have — which reads as the
/// graph being wrong rather than as one missing line.
/// </para>
/// <para>
/// The graph shape is asserted separately because the table and the drawing are
/// two statements of one fact, and either can be edited without the other.
/// </para>
/// </remarks>
public sealed class BuiltInRuleDescriptorsTests
{
    /// <summary>
    /// Every rule in <c>Preflight.Rules</c>, found the way the engine finds
    /// them.
    /// </summary>
    /// <remarks>
    /// By reflection over the assembly rather than a hand-written list, so a
    /// seventh rule added without a row in the table fails the count below
    /// instead of quietly joining the set.
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
    public void Discovery_FindsExactlyTheSixRulesOfSectionNine()
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
    /// The table, row by row.
    /// </summary>
    /// <remarks>
    /// <c>gating</c> is <c>false</c> on the leaves, and stating it explicitly
    /// stops anyone reading <c>true</c> as meaning something. The same
    /// reasoning applies to asserting it: a row that is merely inherited is a
    /// row nobody decided.
    /// </remarks>
    [Theory]
    [InlineData("core.workspace.toolchain", ValidationStage.Workspace, "", true, true)]
    [InlineData("core.workspace.dependencies", ValidationStage.Workspace, "core.workspace.toolchain", true, false)]
    [InlineData("core.presubmit.forbidden-paths", ValidationStage.PreSubmit, "", true, false)]
    [InlineData("core.presubmit.large-file", ValidationStage.PreSubmit, "", true, false)]
    [InlineData("core.build.configuration", ValidationStage.BuildReadiness, "core.workspace.toolchain", true, true)]
    [InlineData("core.build.compile-probe", ValidationStage.BuildReadiness, "core.build.configuration", true, false)]
    public void Descriptor_MatchesTheDocumentedTable(
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
    /// The documented graph.
    /// </summary>
    /// <remarks>
    /// Two independent roots at pre-submit, and a chain three deep ending at
    /// the expensive rule. That chain is the argument for the whole graph:
    /// <c>compile-probe</c> runs only if the two cheap rules before it passed.
    /// </remarks>
    [Fact]
    public void Descriptors_ProduceTheDocumentedGraph()
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
    /// indistinguishable from a rule the policy disabled — so the wrong report
    /// is produced for the wrong reason and nobody looks at the descriptor.
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
