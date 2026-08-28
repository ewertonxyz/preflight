namespace Preflight.Core.Tests.Graph;

using Preflight.Abstractions;
using Preflight.Core;
using static Preflight.Core.Tests.Graph.GraphFixture;

/// <summary>
/// Fixes cycle detection, including the two things the rule graph
/// asks for beyond "there is a cycle": the full path in order, and a distinct
/// treatment for a rule depending on itself.
/// </summary>
/// <remarks>
/// A seven-node cycle reported as <c>cycle detected</c> costs half an hour to
/// whoever has to fix it, so the whole chain is listed. The self-dependency
/// case gets its own message, because it is almost always a copy-paste of the
/// wrong id into <c>DependsOn</c>.
/// </remarks>
public sealed class RuleGraphCycleDetectionTests
{
    /// <remarks>
    /// The assertion is on the error <em>type</em>, not merely that something
    /// was thrown. A generic post-Kahn "these nodes never reached in-degree
    /// zero" detector also catches a self-dependency and would emit the
    /// generic cycle message; a test asserting only "throws" passes against
    /// both, which would let the distinct message the design asks for
    /// disappear unnoticed.
    /// </remarks>
    [Fact]
    public void Build_WithSelfDependency_ThrowsSelfDependencyNotGenericCycle()
    {
        var exception = Should.Throw<GraphValidationException>(
            () => RuleGraph.Build([Rule("core.a.alpha", "core.a.alpha")]));

        var error = exception.Errors.ShouldHaveSingleItem();

        error.ShouldBeOfType<GraphValidationError.SelfDependency>()
            .RuleId.ShouldBe(new RuleId("core.a.alpha"));
        error.ShouldNotBeOfType<GraphValidationError.CycleDetected>();
        error.Message.ShouldContain("itself");
    }

    [Fact]
    public void Build_WithTwoNodeCycle_ThrowsCycleDetectedWithFullPath()
    {
        var exception = Should.Throw<GraphValidationException>(() => RuleGraph.Build([
            Rule("core.a.alpha", "core.a.bravo"),
            Rule("core.a.bravo", "core.a.alpha"),
        ]));

        var cycle = exception.Errors.ShouldHaveSingleItem().ShouldBeOfType<GraphValidationError.CycleDetected>();

        cycle.Path.Count.ShouldBe(3);
        cycle.Path[0].ShouldBe(cycle.Path[^1]);
        cycle.Path.Take(2).Select(id => id.Value).OrderBy(value => value, StringComparer.Ordinal)
            .ShouldBe(["core.a.alpha", "core.a.bravo"]);
    }

    /// <remarks>
    /// Seven nodes is the design's own example of the message it does not
    /// want. The path is checked edge by edge against the declared
    /// <c>DependsOn</c> so that a lexical or insertion-order listing of the
    /// leftover nodes cannot pass: it has to be the real walk.
    /// </remarks>
    [Fact]
    public void Build_WithSevenNodeCycle_PathContainsEachNodeOnceInRealGraphOrderPlusClosure()
    {
        var ids = Enumerable.Range(0, 7).Select(i => $"core.ring.n{i}").ToArray();
        var descriptors = ids.Select((id, i) => Rule(id, ids[(i + 1) % ids.Length])).ToArray();

        var exception = Should.Throw<GraphValidationException>(() => RuleGraph.Build(descriptors));
        var cycle = exception.Errors.ShouldHaveSingleItem().ShouldBeOfType<GraphValidationError.CycleDetected>();

        cycle.Path.Count.ShouldBe(8);
        cycle.Path[0].ShouldBe(cycle.Path[^1]);
        cycle.Path.Take(7).Select(id => id.Value).Distinct().Count().ShouldBe(7);

        var dependsOn = descriptors.ToDictionary(d => d.Id, d => d.DependsOn[0]);

        for (var i = 0; i < cycle.Path.Count - 1; i++)
        {
            dependsOn[cycle.Path[i]].ShouldBe(cycle.Path[i + 1], "The printed path must follow real edges.");
        }
    }

    [Fact]
    public void Build_WithCycleAmongSomeNodesAndAcyclicOthers_ReportsOnlyTheCyclicNodes()
    {
        var exception = Should.Throw<GraphValidationException>(() => RuleGraph.Build([
            Rule("core.a.alpha", "core.a.bravo"),
            Rule("core.a.bravo", "core.a.alpha"),
            Rule("core.z.yankee", "core.z.zulu"),
            Rule("core.z.zulu"),
        ]));

        var cycle = exception.Errors.ShouldHaveSingleItem().ShouldBeOfType<GraphValidationError.CycleDetected>();

        cycle.Path.Select(id => id.Value).ShouldNotContain("core.z.yankee");
        cycle.Path.Select(id => id.Value).ShouldNotContain("core.z.zulu");
    }

    /// <remarks>
    /// Accumulating rather than stopping at the first problem is the same call
    /// the policy validator makes in the policy layer: someone fixing a rule set should
    /// see everything wrong with it in one pass, not discover the second
    /// defect only after fixing the first.
    /// </remarks>
    [Fact]
    public void Build_WithCycleInOnePartAndMissingDependencyInAnother_ReportsBothErrors()
    {
        var exception = Should.Throw<GraphValidationException>(() => RuleGraph.Build([
            Rule("core.a.alpha", "core.a.bravo"),
            Rule("core.a.bravo", "core.a.alpha"),
            Rule("core.z.yankee", "core.z.nowhere"),
        ]));

        exception.Errors.Count.ShouldBe(2);
        exception.Errors.ShouldContain(error => error is GraphValidationError.CycleDetected);
        exception.Errors.ShouldContain(error => error is GraphValidationError.MissingDependency);
    }

    /// <remarks>
    /// A dangling edge is ignored for the purpose of cycle detection so both
    /// analyses complete in the same pass. Without that, the missing target
    /// would leave the node permanently at non-zero in-degree and the real
    /// cycle beside it could be reported as something else, or masked.
    /// </remarks>
    [Fact]
    public void Build_WithDanglingEdgeSharingANodeWithACycle_StillDetectsTheCycle()
    {
        var exception = Should.Throw<GraphValidationException>(() => RuleGraph.Build([
            Rule("core.a.alpha", "core.a.bravo", "core.a.nowhere"),
            Rule("core.a.bravo", "core.a.alpha"),
        ]));

        exception.Errors.ShouldContain(error => error is GraphValidationError.CycleDetected);
        exception.Errors.ShouldContain(error => error is GraphValidationError.MissingDependency);
    }
}
