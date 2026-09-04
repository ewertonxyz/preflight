namespace Preflight.Core.Tests.Execution;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core;
using Preflight.Core.Execution;
using Preflight.Core.Graph;
using static Preflight.Core.Tests.Graph.GraphFixture;

/// <summary>
/// Fixes the presentation order of the determinism guarantee: topological
/// level ascending, then <see cref="RuleId"/> ordinal, then findings in the
/// order the rule produced them.
/// </summary>
/// <remarks>
/// <para>
/// This is one of the two tests the test strategy singles out. It guards against a
/// change that emits results in completion order, which would break only
/// intermittently and therefore be written off as flaky by whoever had not
/// read this comment. What it protects is the ability to diff two CI logs and
/// the golden files of the console reporter.
/// </para>
/// <para>
/// The ordering itself is tested with no concurrency at all: the input is
/// shuffled deliberately, so the assertion is about the sort and nothing else.
/// Proving that the sort survives real parallel execution belongs to
/// <c>RuleExecutorTests</c>, where the completion order is forced rather than
/// hoped for.
/// </para>
/// </remarks>
public sealed class ExecutionOrderingTests
{
    [Fact]
    public void Sort_OrdersByTopologicalLevelAscending()
    {
        var graph = RuleGraph.Build([
            Rule("core.a.alpha", "core.a.bravo"),
            Rule("core.a.bravo", "core.a.charlie"),
            Rule("core.a.charlie"),
        ]);

        var sorted = ExecutionOrdering.Sort(
            [Execution("core.a.alpha"), Execution("core.a.charlie"), Execution("core.a.bravo")], graph);

        sorted.Select(execution => execution.RuleId.Value)
            .ShouldBe(["core.a.charlie", "core.a.bravo", "core.a.alpha"]);
    }

    /// <remarks>
    /// The ids are chosen so ordinal and numeric order disagree: ordinal puts
    /// <c>rule-10</c> before <c>rule-2</c>.
    /// </remarks>
    [Fact]
    public void Sort_WithinALevel_OrdersByRuleIdOrdinal()
    {
        var graph = RuleGraph.Build([Rule("core.a.rule-2"), Rule("core.a.rule-10"), Rule("core.a.rule-1")]);

        var sorted = ExecutionOrdering.Sort(
            [Execution("core.a.rule-2"), Execution("core.a.rule-1"), Execution("core.a.rule-10")], graph);

        sorted.Select(execution => execution.RuleId.Value)
            .ShouldBe(["core.a.rule-1", "core.a.rule-10", "core.a.rule-2"]);
    }

    [Fact]
    public void Sort_LeavesFindingsInTheOrderTheRuleProducedThem()
    {
        var graph = RuleGraph.Build([Rule("core.a.alpha")]);
        var execution = Execution("core.a.alpha") with
        {
            Findings =
            [
                new Finding { Message = "third" },
                new Finding { Message = "first" },
            ],
        };

        ExecutionOrdering.Sort([execution], graph)[0].Findings
            .Select(finding => finding.Message).ShouldBe(["third", "first"]);
    }

    /// <remarks>
    /// The root cause has to precede the symptom, which the console report relies on
    /// for its console layout and gets for free from level ordering — but only
    /// if skipped entries are sorted by the same rule instead of appended in a
    /// trailing block.
    /// </remarks>
    [Fact]
    public void Sort_PlacesSkippedEntriesAtTheirOwnTopologicalPositionNotInATrailingBlock()
    {
        var graph = RuleGraph.Build([
            Rule("core.a.alpha", "core.a.bravo"),
            Rule("core.a.bravo", "core.a.charlie"),
            Rule("core.a.charlie"),
        ]);

        var sorted = ExecutionOrdering.Sort(
            [
                Execution("core.a.alpha", RuleStatus.Skipped),
                Execution("core.a.bravo", RuleStatus.Skipped),
                Execution("core.a.charlie", RuleStatus.Failed),
            ],
            graph);

        sorted.Select(execution => execution.RuleId.Value)
            .ShouldBe(["core.a.charlie", "core.a.bravo", "core.a.alpha"]);
    }

    [Fact]
    public void Sort_WithTheInputInAnyOrder_ProducesTheSameSequence()
    {
        var graph = RuleGraph.Build([
            Rule("core.a.alpha", "core.a.bravo", "core.a.charlie"),
            Rule("core.a.bravo", "core.a.delta"),
            Rule("core.a.charlie", "core.a.delta"),
            Rule("core.a.delta"),
        ]);

        var executions = new[]
        {
            Execution("core.a.alpha"),
            Execution("core.a.bravo"),
            Execution("core.a.charlie"),
            Execution("core.a.delta"),
        };

        var forward = ExecutionOrdering.Sort(executions, graph).Select(execution => execution.RuleId.Value);
        var reversed = ExecutionOrdering.Sort([.. executions.Reverse()], graph)
            .Select(execution => execution.RuleId.Value);

        forward.ShouldBe(reversed);
    }

    /// <remarks>
    /// The level comes from the full discovered graph, not from the executed
    /// subset. A run that executes only part of the graph leaves gaps in the
    /// level numbers, and gaps do not affect an ordering — whereas recomputing
    /// levels per run would give the same rule a different position depending
    /// on which stage was asked for.
    /// </remarks>
    [Fact]
    public void Sort_UsesTheLevelOfTheFullGraphNotTheExecutedSubset()
    {
        var graph = RuleGraph.Build([
            Rule("core.a.alpha", "core.a.bravo"),
            Rule("core.a.bravo", "core.a.charlie"),
            Rule("core.a.charlie"),
        ]);

        var sorted = ExecutionOrdering.Sort([Execution("core.a.alpha"), Execution("core.a.charlie")], graph);

        sorted.Select(execution => execution.RuleId.Value).ShouldBe(["core.a.charlie", "core.a.alpha"]);
    }

    private static RuleExecution Execution(string id, RuleStatus status = RuleStatus.Passed) => new()
    {
        RuleId = new RuleId(id),
        Status = status,
        EffectiveSeverity = Severity.Error,
        Blocking = true,
        Gating = true,
        Duration = TimeSpan.Zero,
    };
}
