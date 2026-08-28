namespace Preflight.Core;

using Preflight.Abstractions;

/// <summary>
/// Works out who is skipped, why, and which rule to blame.
/// </summary>
/// <remarks>
/// <para>
/// When a rule fails or errors
/// <em>with gating enabled in effective policy</em>, everything that
/// transitively depends on it is skipped.
/// </para>
/// <para>
/// The decision that matters is the attribution: it names the original failure,
/// never the immediate parent. The design prints both formats side by side and
/// says of the wrong one that it "manda o desenvolvedor investigar o lugar
/// errado". Resolving through the skipped nodes until a real terminal ancestor
/// appears is what buys that.
/// </para>
/// <para>
/// Pure by design — no async, no scheduling, no clock. Every case here would
/// otherwise have to be provoked through a parallel run, which trades a short
/// test for a long one with a race inside it.
/// </para>
/// </remarks>
public static class SkipPropagation
{
    /// <summary>
    /// One skipped rule: why, and the terminal ancestors responsible,
    /// shallowest first.
    /// </summary>
    public sealed record SkipAttribution
    {
        public required RuleId RuleId { get; init; }

        public required SkipReason Reason { get; init; }

        public required IReadOnlyList<RuleId> SkippedBecauseOf { get; init; }
    }

    public static IReadOnlyDictionary<RuleId, SkipAttribution> Compute(
        RuleGraph graph,
        IReadOnlyDictionary<RuleId, RuleStatus> terminalStatuses,
        IReadOnlyDictionary<RuleId, RulePolicySnapshot> snapshots,
        IReadOnlyList<ExecutionSet.SkippedByDisabledDependency> disabled,
        bool noSkip)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(terminalStatuses);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(disabled);

        var levelOf = LevelsOf(graph);
        var attributions = new Dictionary<RuleId, SkipAttribution>();

        // A disabled dependency is unaffected by --no-skip. That flag exists to
        // show the whole picture past a *failure*; a disabled rule genuinely did
        // not run, so running its dependent would be validating against a
        // prerequisite that was never established.
        foreach (var entry in disabled)
        {
            attributions[entry.RuleId] = new SkipAttribution
            {
                RuleId = entry.RuleId,
                Reason = SkipReason.DependencyDisabled,
                SkippedBecauseOf = ByDepth(entry.DisabledDependencies, levelOf),
            };
        }

        if (noSkip)
        {
            return attributions;
        }

        var gatingTerminals = terminalStatuses
            .Where(entry => entry.Value is RuleStatus.Failed or RuleStatus.Errored)
            .Where(entry => snapshots.TryGetValue(entry.Key, out var snapshot) && snapshot.Gating)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        foreach (var terminalId in gatingTerminals.Keys)
        {
            foreach (var dependent in graph.TransitiveDependentsOf(terminalId))
            {
                if (attributions.TryGetValue(dependent, out var existing) &&
                    existing.Reason == SkipReason.DependencyDisabled)
                {
                    continue;
                }

                var causes = graph.TransitiveDependenciesOf(dependent)
                    .Where(gatingTerminals.ContainsKey)
                    .ToArray();

                var ordered = ByDepth(causes, levelOf);

                attributions[dependent] = new SkipAttribution
                {
                    RuleId = dependent,
                    Reason = ReasonOf(gatingTerminals[ordered[0]]),
                    SkippedBecauseOf = ordered,
                };
            }
        }

        return attributions;
    }

    /// <remarks>
    /// "Shallowest first" means lowest topological level — the further upstream
    /// a failure is, the likelier it is the real cause. The other reading,
    /// distance in edges from the skipped node, would produce exactly the
    /// immediate-parent attribution this exists to avoid.
    ///
    /// The ordinal tie-break is not decoration: without it the order of equally
    /// deep ancestors comes out of a hash set, and the report stops being
    /// diffable intermittently.
    /// </remarks>
    private static IReadOnlyList<RuleId> ByDepth(
        IReadOnlyList<RuleId> causes, Dictionary<RuleId, int> levelOf) =>
        [.. causes
            .OrderBy(id => levelOf.TryGetValue(id, out var level) ? level : int.MaxValue)
            .ThenBy(id => id.Value, StringComparer.Ordinal)];

    /// <remarks>
    /// Built here from <c>Levels</c> rather than asked of the graph: 7.1 fixes
    /// the graph's surface at three members, and the test that pins it exists
    /// to make a fourth one noisy.
    /// </remarks>
    private static Dictionary<RuleId, int> LevelsOf(RuleGraph graph)
    {
        var levelOf = new Dictionary<RuleId, int>();

        for (var level = 0; level < graph.Levels.Count; level++)
        {
            foreach (var id in graph.Levels[level])
            {
                levelOf[id] = level;
            }
        }

        return levelOf;
    }

    private static SkipReason ReasonOf(RuleStatus status) =>
        status is RuleStatus.Errored ? SkipReason.DependencyErrored : SkipReason.DependencyFailed;
}
