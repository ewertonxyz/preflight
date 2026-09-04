namespace Preflight.Core.Tests.Graph;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core;
using Preflight.Core.Graph;
using static Preflight.Core.Tests.Graph.GraphFixture;

/// <summary>
/// Fixes the topological levelling of <see cref="RuleGraph.Build"/>.
/// </summary>
/// <remarks>
/// the rule graph: <c>Build</c> runs Kahn's algorithm by levels,
/// and within a level the id order is stable — ordinal by
/// <see cref="RuleId"/>. That ordering does not affect execution, which is
/// parallel; it affects <c>preflight graph</c>, whose output has to be
/// diffable between two runs, which is determinism extended to the report.
/// </remarks>
public sealed class RuleGraphLevelsTests
{
    [Fact]
    public void Build_WithNoDescriptors_ReturnsEmptyLevels()
    {
        RuleGraph.Build([]).Levels.ShouldBeEmpty();
    }

    [Fact]
    public void Build_WithSingleDescriptorAndNoDependencies_ReturnsOneLevelWithOneRule()
    {
        var graph = RuleGraph.Build([Rule("core.a.alpha")]);

        graph.Levels.ShouldHaveSingleItem();
        graph.Levels[0].ShouldBe([new RuleId("core.a.alpha")]);
    }

    [Fact]
    public void Build_WithSimpleChain_OrdersLevelsFromLeafToRoot()
    {
        var graph = RuleGraph.Build([
            Rule("core.a.alpha", "core.a.bravo"),
            Rule("core.a.bravo", "core.a.charlie"),
            Rule("core.a.charlie"),
        ]);

        LevelsAsStrings(graph).ShouldBe([
            ["core.a.charlie"],
            ["core.a.bravo"],
            ["core.a.alpha"],
        ]);
    }

    [Fact]
    public void Build_WithDiamond_PutsBothMiddleNodesInTheSameLevel()
    {
        var graph = RuleGraph.Build([
            Rule("core.a.alpha", "core.a.bravo", "core.a.charlie"),
            Rule("core.a.bravo", "core.a.delta"),
            Rule("core.a.charlie", "core.a.delta"),
            Rule("core.a.delta"),
        ]);

        LevelsAsStrings(graph).ShouldBe([
            ["core.a.delta"],
            ["core.a.bravo", "core.a.charlie"],
            ["core.a.alpha"],
        ]);
    }

    [Fact]
    public void Build_WithDisconnectedGraph_LevelsEachComponentIndependently()
    {
        var graph = RuleGraph.Build([
            Rule("core.a.alpha", "core.a.bravo"),
            Rule("core.a.bravo"),
            Rule("core.z.yankee", "core.z.zulu"),
            Rule("core.z.zulu"),
        ]);

        LevelsAsStrings(graph).ShouldBe([
            ["core.a.bravo", "core.z.zulu"],
            ["core.a.alpha", "core.z.yankee"],
        ]);
    }

    /// <remarks>
    /// The ids here are chosen so ordinal and any numeric-aware or
    /// culture-aware ordering disagree: ordinal puts <c>rule-10</c> before
    /// <c>rule-2</c>, because <c>'1' &lt; '2'</c>. An implementation that
    /// reached the right answer for plain alphabetic ids by luck fails here.
    /// </remarks>
    [Fact]
    public void Build_WithinALevel_OrdersRuleIdsByStringComparerOrdinalNotCulture()
    {
        var graph = RuleGraph.Build([
            Rule("core.a.rule-2"),
            Rule("core.a.rule-10"),
            Rule("core.a.rule-1"),
        ]);

        graph.Levels.ShouldHaveSingleItem();
        LevelsAsStrings(graph)[0].ShouldBe(["core.a.rule-1", "core.a.rule-10", "core.a.rule-2"]);
    }

    [Fact]
    public void Build_CalledTwiceWithSameDescriptors_ProducesStructurallyIdenticalLevels()
    {
        var descriptors = new[]
        {
            Rule("core.a.alpha", "core.a.bravo", "core.a.charlie"),
            Rule("core.a.bravo", "core.a.delta"),
            Rule("core.a.charlie", "core.a.delta"),
            Rule("core.a.delta"),
        };

        LevelsAsStrings(RuleGraph.Build(descriptors)).ShouldBe(LevelsAsStrings(RuleGraph.Build(descriptors)));
    }

    /// <remarks>
    /// Guards the failure the ordinal sort exists to prevent: a queue fed in
    /// discovery order and never re-sorted passes whenever the input happens
    /// to arrive sorted, and only misbehaves against a real discovery order,
    /// which nothing guarantees is alphabetical.
    /// </remarks>
    [Fact]
    public void Build_WithDescriptorsSuppliedInDifferentListOrder_ProducesIdenticalLevels()
    {
        var forward = new[]
        {
            Rule("core.a.alpha", "core.a.bravo", "core.a.charlie"),
            Rule("core.a.bravo", "core.a.delta"),
            Rule("core.a.charlie", "core.a.delta"),
            Rule("core.a.delta"),
        };

        LevelsAsStrings(RuleGraph.Build(forward)).ShouldBe(LevelsAsStrings(RuleGraph.Build([.. forward.Reverse()])));
    }

    /// <remarks>
    /// A duplicate id silently overwriting an entry in a
    /// <c>Dictionary&lt;RuleId, …&gt;</c> would make a rule vanish from the graph
    /// with no error anywhere — a rule that never runs and never reports, in a
    /// tool whose entire job is to report. The two descriptors below differ in
    /// stage so the test cannot pass by the two being interchangeable.
    /// </remarks>
    [Fact]
    public void Build_WithDuplicateRuleId_ThrowsNamingTheDuplicateId()
    {
        var exception = Should.Throw<GraphValidationException>(() => RuleGraph.Build([
            Rule("core.a.alpha", ValidationStage.PreSubmit),
            Rule("core.a.alpha", ValidationStage.Workspace),
        ]));

        exception.Errors.ShouldHaveSingleItem()
            .ShouldBeOfType<GraphValidationError.DuplicateRuleId>()
            .RuleId.ShouldBe(new RuleId("core.a.alpha"));
    }

    /// <remarks>
    /// <see cref="RuleGraph"/> knows nothing about stages: an edge is an edge.
    /// Stage only ever decides which rules seed the closure, and that decision
    /// lives in <see cref="ExecutionSet"/>. and stage selection.
    /// </remarks>
    [Fact]
    public void Build_WithDependencyCrossingStages_TreatsItAsAnOrdinaryEdge()
    {
        var graph = RuleGraph.Build([
            Rule("core.build.configuration", ValidationStage.BuildReadiness, "core.workspace.toolchain"),
            Rule("core.workspace.toolchain", ValidationStage.Workspace),
        ]);

        LevelsAsStrings(graph).ShouldBe([
            ["core.workspace.toolchain"],
            ["core.build.configuration"],
        ]);
    }

    private static string[][] LevelsAsStrings(RuleGraph graph) =>
        [.. graph.Levels.Select(level => level.Select(id => id.Value).ToArray())];
}
