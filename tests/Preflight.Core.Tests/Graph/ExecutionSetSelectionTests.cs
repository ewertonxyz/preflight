namespace Preflight.Core.Tests.Graph;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core;
using Preflight.Core.Graph;
using Preflight.Core.Policy;
using static Preflight.Core.Tests.Graph.GraphFixture;

/// <summary>
/// Fixes the execution-set selection of stage selection and its
/// four dependency states.
/// </summary>
/// <remarks>
/// <para>
/// The rule the whole design insists on: <em>the stage
/// picks the roots, not the set</em>. The naive alternative — filter by stage,
/// then build the graph from what is left — drops a cross-stage dependency and
/// then skips the rule that needed it, in a run where the user asked for
/// exactly that check.
/// </para>
/// <para>
/// "Dependency in another stage" and "dependency disabled by policy" are false
/// friends worth keeping apart: in code both begin as
/// "this <c>DependsOn</c> edge points at a descriptor that is not a root of
/// this run", and a single boolean would collapse them and get one backwards.
/// They are tested side by side here for that reason, including the edge that
/// is both at once.
/// </para>
/// </remarks>
public sealed class ExecutionSetSelectionTests
{
    private static EffectivePolicy PolicyDisabling(IReadOnlyList<RuleDescriptor> descriptors, params string[] disabledIds)
    {
        if (disabledIds.Length == 0)
        {
            return EffectivePolicy.Build(descriptors, pipeline: null, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);
        }

        var rules = string.Join(",", disabledIds.Select(id => $"\"{id}\": {{ \"enabled\": false }}"));
        var production = PolicyDocument.Parse($$"""{ "schemaVersion": 1, "rules": { {{rules}} } }""", "atlas.json");

        return EffectivePolicy.Build(descriptors, production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);
    }

    private static ExecutionSet Select(
        IReadOnlyList<RuleDescriptor> descriptors, ValidationStage stage, params string[] disabledIds) =>
        ExecutionSet.Select(RuleGraph.Build(descriptors), descriptors, stage, PolicyDisabling(descriptors, disabledIds));

    private static string[] Values(IEnumerable<RuleId> ids) => [.. ids.Select(id => id.Value)];

    [Fact]
    public void Select_WithSameStageDependency_PullsItInAsAnOrdinaryEdge()
    {
        var descriptors = new[]
        {
            Rule("core.presubmit.alpha", ValidationStage.PreSubmit, "core.presubmit.bravo"),
            Rule("core.presubmit.bravo", ValidationStage.PreSubmit),
        };

        var set = Select(descriptors, ValidationStage.PreSubmit);

        Values(set.ToExecute).ShouldBe(["core.presubmit.alpha", "core.presubmit.bravo"]);
        set.Skipped.ShouldBeEmpty();
    }

    /// <remarks>
    /// The worked example this project keeps coming back to — a build-readiness
    /// run that has to pull a workspace-stage toolchain rule in with it —
    /// reproduced with its own ids so nobody "simplifies" it later without
    /// understanding what the stage actually selects.
    /// </remarks>
    [Fact]
    public void Select_WithTheWorkedExample_PullsInCrossStageToolchainAndExcludesUnrelatedRules()
    {
        var descriptors = WorkedExample();

        var set = Select(descriptors, ValidationStage.BuildReadiness);

        Values(set.ToExecute).ShouldBe([
            "core.build.compile-probe",
            "core.build.configuration",
            "core.workspace.toolchain",
        ]);
        Values(set.ToExecute).ShouldNotContain("core.workspace.dependencies");
        Values(set.ToExecute).ShouldNotContain("core.presubmit.large-file");
        Values(set.Skipped.Select(skipped => skipped.RuleId)).ShouldNotContain("core.workspace.dependencies");
    }

    [Fact]
    public void Select_WithDirectDisabledDependency_ExcludesItAndSkipsTheDependent()
    {
        var descriptors = new[]
        {
            Rule("core.presubmit.alpha", ValidationStage.PreSubmit, "core.presubmit.bravo"),
            Rule("core.presubmit.bravo", ValidationStage.PreSubmit),
        };

        var set = Select(descriptors, ValidationStage.PreSubmit, "core.presubmit.bravo");

        Values(set.ToExecute).ShouldNotContain("core.presubmit.bravo");
        Values(set.ToExecute).ShouldNotContain("core.presubmit.alpha");

        var skipped = set.Skipped.ShouldHaveSingleItem();
        skipped.RuleId.ShouldBe(new RuleId("core.presubmit.alpha"));
        Values(skipped.DisabledDependencies).ShouldBe(["core.presubmit.bravo"]);
    }

    /// <remarks>
    /// Skip attribution's root-cause rule, in the one form that is knowable without
    /// running anything: the attribution names the disabled rule, not the
    /// intermediate rule that was itself skipped. Pointing at the immediate
    /// parent sends the developer to fix the wrong file.
    /// </remarks>
    [Fact]
    public void Select_WithTransitivelyDisabledDependency_AttributesSkipToTheDisabledRuleNotTheImmediateParent()
    {
        var descriptors = new[]
        {
            Rule("core.presubmit.alpha", ValidationStage.PreSubmit, "core.presubmit.bravo"),
            Rule("core.presubmit.bravo", ValidationStage.PreSubmit, "core.presubmit.charlie"),
            Rule("core.presubmit.charlie", ValidationStage.PreSubmit),
        };

        var set = Select(descriptors, ValidationStage.PreSubmit, "core.presubmit.charlie");

        set.Skipped.Count.ShouldBe(2);

        foreach (var skipped in set.Skipped)
        {
            Values(skipped.DisabledDependencies).ShouldBe(
                ["core.presubmit.charlie"],
                "Attribution points at the disabled root, never at an intermediate skipped rule.");
        }
    }

    [Fact]
    public void Select_WithADisabledRootMatchingRequestedStage_ExcludesItEntirelyFromBothLists()
    {
        var descriptors = new[] { Rule("core.presubmit.alpha", ValidationStage.PreSubmit) };

        var set = Select(descriptors, ValidationStage.PreSubmit, "core.presubmit.alpha");

        set.ToExecute.ShouldBeEmpty();
        set.Skipped.ShouldBeEmpty();
    }

    /// <remarks>
    /// A deliberate divergence from the obvious ordering, which computes the
    /// closure from every stage-matching rule and only then subtracts the
    /// disabled ones. Done that way, disabling a rule leaves its exclusive
    /// dependency running — and able to fail the run — which empties out what
    /// disabling is for. The roots are filtered first instead.
    /// </remarks>
    [Fact]
    public void Select_WithADisabledRootsExclusiveDependency_ExcludesTheDependencyEntirely()
    {
        // The dependency sits in another stage on purpose: were it a rule of
        // the requested stage it would be a root in its own right, and the
        // test would pass without ever exercising the decision.
        var descriptors = new[]
        {
            Rule("core.presubmit.alpha", ValidationStage.PreSubmit, "core.workspace.bravo"),
            Rule("core.workspace.bravo", ValidationStage.Workspace),
        };

        var set = Select(descriptors, ValidationStage.PreSubmit, "core.presubmit.alpha");

        set.ToExecute.ShouldBeEmpty();
        set.Skipped.ShouldBeEmpty();
    }

    /// <remarks>
    /// The counterpart of the test above: the exclusion is scoped to what only
    /// the disabled root needed. A dependency someone else still needs stays.
    /// </remarks>
    [Fact]
    public void Select_WithADisabledRootsDependencySharedByALiveRoot_StillIncludesIt()
    {
        // Same shape as the test above — the shared rule is in another stage,
        // so it is only ever present by being depended on — except that a live
        // root needs it too.
        var descriptors = new[]
        {
            Rule("core.presubmit.alpha", ValidationStage.PreSubmit, "core.workspace.shared"),
            Rule("core.presubmit.delta", ValidationStage.PreSubmit, "core.workspace.shared"),
            Rule("core.workspace.shared", ValidationStage.Workspace),
        };

        var set = Select(descriptors, ValidationStage.PreSubmit, "core.presubmit.alpha");

        Values(set.ToExecute).ShouldBe(["core.presubmit.delta", "core.workspace.shared"]);
    }

    /// <remarks>
    /// The sharpest point of that false friend: one edge that is both
    /// cross-stage and disabled. Being reachable from another stage must
    /// never let a rule bypass the enabled check.
    /// </remarks>
    [Fact]
    public void Select_WithADependencyThatIsBothCrossStageAndDisabled_DisabledStateWins()
    {
        var descriptors = new[]
        {
            Rule("core.build.configuration", ValidationStage.BuildReadiness, "core.workspace.toolchain"),
            Rule("core.workspace.toolchain", ValidationStage.Workspace),
        };

        var set = Select(descriptors, ValidationStage.BuildReadiness, "core.workspace.toolchain");

        Values(set.ToExecute).ShouldNotContain("core.workspace.toolchain");

        var skipped = set.Skipped.ShouldHaveSingleItem();
        skipped.RuleId.ShouldBe(new RuleId("core.build.configuration"));
        Values(skipped.DisabledDependencies).ShouldBe(["core.workspace.toolchain"]);
    }

    /// <remarks>
    /// Walking dependents instead of dependencies over-includes in silence:
    /// the set is wrong, but the graph builds and the run happens. Here
    /// <c>core.presubmit.charlie</c> depends on the same rule a root needs,
    /// and must not be dragged in by that coincidence.
    /// </remarks>
    [Fact]
    public void Select_UsingDependenciesNotDependents_NeverPullsInARuleThatDependsOnAClosureMember()
    {
        var descriptors = new[]
        {
            Rule("core.presubmit.alpha", ValidationStage.PreSubmit, "core.presubmit.bravo"),
            Rule("core.presubmit.bravo", ValidationStage.PreSubmit),
            Rule("core.workspace.charlie", ValidationStage.Workspace, "core.presubmit.bravo"),
        };

        var set = Select(descriptors, ValidationStage.PreSubmit);

        Values(set.ToExecute).ShouldBe(["core.presubmit.alpha", "core.presubmit.bravo"]);
        Values(set.ToExecute).ShouldNotContain("core.workspace.charlie");
    }

    [Fact]
    public void Select_CalledTwiceForTheSameStage_ProducesIdenticalResults()
    {
        var descriptors = WorkedExample();

        var first = Select(descriptors, ValidationStage.BuildReadiness);
        var second = Select(descriptors, ValidationStage.BuildReadiness);

        Values(first.ToExecute).ShouldBe(Values(second.ToExecute));
    }

    [Fact]
    public void Select_ToExecuteAndSkipped_AreOrderedByRuleIdOrdinal()
    {
        var descriptors = new[]
        {
            Rule("core.a.rule-2", ValidationStage.PreSubmit, "core.a.gate"),
            Rule("core.a.rule-10", ValidationStage.PreSubmit, "core.a.gate"),
            Rule("core.a.rule-1", ValidationStage.PreSubmit, "core.a.gate"),
            Rule("core.a.gate", ValidationStage.PreSubmit),
        };

        var enabled = Select(descriptors, ValidationStage.PreSubmit);
        Values(enabled.ToExecute).ShouldBe(["core.a.gate", "core.a.rule-1", "core.a.rule-10", "core.a.rule-2"]);

        var disabled = Select(descriptors, ValidationStage.PreSubmit, "core.a.gate");
        Values(disabled.Skipped.Select(skipped => skipped.RuleId))
            .ShouldBe(["core.a.rule-1", "core.a.rule-10", "core.a.rule-2"]);
    }

    private static RuleDescriptor[] WorkedExample() =>
    [
        Rule("core.build.configuration", ValidationStage.BuildReadiness, "core.workspace.toolchain"),
        Rule("core.build.compile-probe", ValidationStage.BuildReadiness, "core.build.configuration"),
        Rule("core.workspace.toolchain", ValidationStage.Workspace),
        Rule("core.workspace.dependencies", ValidationStage.Workspace, "core.workspace.toolchain"),
        Rule("core.presubmit.large-file", ValidationStage.PreSubmit),
    ];
}
