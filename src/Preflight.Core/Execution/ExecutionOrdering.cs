namespace Preflight.Core.Execution;

using Preflight.Abstractions.Rules;
using Preflight.Core.Graph;

/// <summary>
/// Puts the executions of a run into the order they are presented in.
/// </summary>
/// <remarks>
/// <para>
/// Parallel execution threatens determinism in a subtle way: the order rules
/// finish in varies between runs, and a report emitted in completion order is
/// not diffable. So presentation order is fixed and independent of it —
/// topological level ascending, then <see cref="RuleId"/> ordinal, with each
/// rule's findings left in the order it produced them.
/// </para>
/// <para>
/// The level comes from the full discovered graph rather than from the executed
/// subset. A partial run leaves gaps in the numbering, and gaps do not affect
/// an ordering, whereas renumbering per run would move the same rule depending
/// on which stage was asked for.
/// </para>
/// </remarks>
public static class ExecutionOrdering
{
    public static IReadOnlyList<RuleExecution> Sort(IReadOnlyList<RuleExecution> executions, RuleGraph graph)
    {
        ArgumentNullException.ThrowIfNull(executions);
        ArgumentNullException.ThrowIfNull(graph);

        var levelOf = new Dictionary<RuleId, int>();

        for (var level = 0; level < graph.Levels.Count; level++)
        {
            foreach (var id in graph.Levels[level])
            {
                levelOf[id] = level;
            }
        }

        return [.. executions
            .OrderBy(execution => levelOf.TryGetValue(execution.RuleId, out var level) ? level : int.MaxValue)
            .ThenBy(execution => execution.RuleId.Value, StringComparer.Ordinal)];
    }
}
