namespace Preflight.Core.Tests.Graph;

using Preflight.Abstractions;
using Preflight.Core;
using static Preflight.Core.Tests.Graph.GraphFixture;

/// <summary>
/// Fixes the two traversal methods of the rule graph:
/// <c>TransitiveDependenciesOf</c>, which implements stage selection's closure,
/// and <c>TransitiveDependentsOf</c>, which implements skip attribution's
/// propagation.
/// </summary>
/// <remarks>
/// Both return ordinal order by <see cref="RuleId"/>, and that is a tested
/// contract rather than an accident of the traversal. Without pinning it,
/// the executor would come to depend on whatever order today's implementation
/// happens to produce, and a later change of traversal strategy — switching
/// depth-first to breadth-first, say — would break the executor without touching a
/// line the executor owns.
/// </remarks>
public sealed class RuleGraphTransitiveClosureTests
{
    private static RuleGraph Diamond() => RuleGraph.Build([
        Rule("core.a.alpha", "core.a.bravo", "core.a.charlie"),
        Rule("core.a.bravo", "core.a.delta"),
        Rule("core.a.charlie", "core.a.delta"),
        Rule("core.a.delta"),
    ]);

    [Fact]
    public void TransitiveDependenciesOf_WithNoDependencies_ReturnsEmpty()
    {
        var graph = RuleGraph.Build([Rule("core.a.alpha")]);

        graph.TransitiveDependenciesOf(new RuleId("core.a.alpha")).ShouldBeEmpty();
    }

    [Fact]
    public void TransitiveDependenciesOf_WithDiamond_ReturnsAllAncestorsOnceOrdinalOrdered()
    {
        Values(Diamond().TransitiveDependenciesOf(new RuleId("core.a.alpha")))
            .ShouldBe(["core.a.bravo", "core.a.charlie", "core.a.delta"]);
    }

    [Fact]
    public void TransitiveDependentsOf_WithDiamond_ReturnsAllDescendantsOnceOrdinalOrdered()
    {
        Values(Diamond().TransitiveDependentsOf(new RuleId("core.a.delta")))
            .ShouldBe(["core.a.alpha", "core.a.bravo", "core.a.charlie"]);
    }

    /// <remarks>
    /// Whoever walks the closure in the wrong direction over-includes in
    /// silence: the graph still builds, the run still happens, and nothing
    /// throws. Asserting the two directions are opposites for the same node is
    /// what makes the mistake visible.
    /// </remarks>
    [Fact]
    public void TransitiveDependentsOf_IsNotTheSameDirectionAsTransitiveDependenciesOf()
    {
        var graph = RuleGraph.Build([
            Rule("core.a.alpha", "core.a.bravo"),
            Rule("core.a.bravo", "core.a.charlie"),
            Rule("core.a.charlie"),
        ]);

        var middle = new RuleId("core.a.bravo");

        Values(graph.TransitiveDependenciesOf(middle)).ShouldBe(["core.a.charlie"]);
        Values(graph.TransitiveDependentsOf(middle)).ShouldBe(["core.a.alpha"]);
    }

    [Fact]
    public void Transitive_WithMixedDigitAndHyphenIds_OrdersByStringComparerOrdinal()
    {
        var graph = RuleGraph.Build([
            Rule("core.a.root", "core.a.rule-2", "core.a.rule-10", "core.a.rule-1"),
            Rule("core.a.rule-1"),
            Rule("core.a.rule-2"),
            Rule("core.a.rule-10"),
        ]);

        Values(graph.TransitiveDependenciesOf(new RuleId("core.a.root")))
            .ShouldBe(["core.a.rule-1", "core.a.rule-10", "core.a.rule-2"]);
    }

    /// <remarks>
    /// Guards an implementation constraint rather than a behaviour: the
    /// traversal must use an explicit stack, not C# call-stack recursion. A
    /// <c>StackOverflowException</c> cannot be caught in.NET, so a recursive
    /// implementation would not fail this test red — it would tear down the
    /// whole test process with no readable assertion, which at a glance looks
    /// like an environment problem rather than a defect.
    /// </remarks>
    [Theory]
    [InlineData(10)]
    [InlineData(500)]
    [InlineData(5000)]
    public void TransitiveDependenciesOf_WithDeepChain_ReturnsFullChainWithoutStackOverflow(int depth)
    {
        var graph = RuleGraph.Build(Chain(depth));

        graph.TransitiveDependenciesOf(new RuleId(ChainId(0))).Count.ShouldBe(depth - 1);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(500)]
    [InlineData(5000)]
    public void TransitiveDependentsOf_WithDeepChain_ReturnsFullChainWithoutStackOverflow(int depth)
    {
        var graph = RuleGraph.Build(Chain(depth));

        graph.TransitiveDependentsOf(new RuleId(ChainId(depth - 1))).Count.ShouldBe(depth - 1);
    }

    [Fact]
    public void TransitiveDependenciesOf_WithAnIdNotInTheGraph_ThrowsKeyNotFoundException()
    {
        var graph = RuleGraph.Build([Rule("core.a.alpha")]);

        Should.Throw<KeyNotFoundException>(() => graph.TransitiveDependenciesOf(new RuleId("core.a.absent")));
    }

    [Fact]
    public void TransitiveDependentsOf_WithAnIdNotInTheGraph_ThrowsKeyNotFoundException()
    {
        var graph = RuleGraph.Build([Rule("core.a.alpha")]);

        Should.Throw<KeyNotFoundException>(() => graph.TransitiveDependentsOf(new RuleId("core.a.absent")));
    }

    /// <remarks>
    /// The contrast that makes the two tests above worth having: an empty list
    /// must mean "exists, has nothing" and never "you asked about something
    /// that is not here". Skip propagation needs to tell those apart.
    /// </remarks>
    [Fact]
    public void TransitiveDependenciesOf_WithALeafNodeThatExists_ReturnsEmptyRatherThanThrowing()
    {
        var graph = RuleGraph.Build([
            Rule("core.a.alpha", "core.a.bravo"),
            Rule("core.a.bravo"),
        ]);

        graph.TransitiveDependenciesOf(new RuleId("core.a.bravo")).ShouldBeEmpty();
    }

    private static string[] Values(IReadOnlyList<RuleId> ids) => [.. ids.Select(id => id.Value)];
}
