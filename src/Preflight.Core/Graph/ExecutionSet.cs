namespace Preflight.Core.Graph;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core.Policy;

/// <summary>
/// Which rules a run actually executes for a requested stage, and which are
/// skipped because something they depend on is disabled.
/// </summary>
/// <remarks>
/// <para>
/// The stage picks the <em>roots</em> of a run, not the set that runs: the
/// dependency closure comes along whatever stage it belongs to. The naive
/// alternative — filter by stage, then build a graph from what is left — drops
/// <c>core.workspace.toolchain</c> out of a <c>build-readiness</c> run and then
/// skips the rule that needed it, in the run where the user asked for precisely
/// that check.
/// </para>
/// <para>
/// This lives beside <see cref="RuleGraph"/> rather than on it because the
/// graph specifies that type with three members and no more, and because stage
/// and policy are exactly the two things the graph is valuable for not knowing.
/// </para>
/// </remarks>
public sealed record ExecutionSet
{
    public required IReadOnlyList<RuleId> ToExecute { get; init; }

    public required IReadOnlyList<SkippedByDisabledDependency> Skipped { get; init; }

    /// <summary>
    /// A rule that will not run because a rule it depends on is disabled by
    /// policy.
    /// </summary>
    /// <remarks>
    /// <see cref="DisabledDependencies"/> names the disabled rules themselves,
    /// never an intermediate rule that was skipped for the same reason.
    /// Attribution points at the root somebody can actually fix, and the
    /// intermediate rule is not it — nobody disabled that one. The report has
    /// to be able to say the cause was configuration rather than failure, in
    /// those words.
    /// </remarks>
    public sealed record SkippedByDisabledDependency
    {
        public required RuleId RuleId { get; init; }

        public required IReadOnlyList<RuleId> DisabledDependencies { get; init; }
    }

    public static ExecutionSet Select(
        RuleGraph graph,
        IReadOnlyList<RuleDescriptor> descriptors,
        ValidationStage stage,
        EffectivePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(policy);

        var disabled = descriptors
            .Where(descriptor => !policy.RuleValue<bool>(descriptor.Id, "enabled").Value)
            .Select(descriptor => descriptor.Id)
            .ToHashSet();

        // A disabled rule is not a root. Doing it the other way round — take
        // the closure first, subtract the disabled after — leaves a disabled
        // rule's exclusive dependency running and able to fail the run. That
        // empties out what disabling is for, so the roots are filtered first.
        var roots = descriptors
            .Where(descriptor => descriptor.Stage == stage && !disabled.Contains(descriptor.Id))
            .Select(descriptor => descriptor.Id)
            .ToArray();

        var candidates = new HashSet<RuleId>(roots);

        foreach (var root in roots)
        {
            candidates.UnionWith(graph.TransitiveDependenciesOf(root));
        }

        var toExecute = new List<RuleId>();
        var skipped = new List<SkippedByDisabledDependency>();

        foreach (var candidate in candidates)
        {
            if (disabled.Contains(candidate))
            {
                continue;
            }

            var disabledDependencies = graph.TransitiveDependenciesOf(candidate)
                .Where(disabled.Contains)
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();

            if (disabledDependencies.Length == 0)
            {
                toExecute.Add(candidate);
                continue;
            }

            skipped.Add(new SkippedByDisabledDependency
            {
                RuleId = candidate,
                DisabledDependencies = disabledDependencies,
            });
        }

        return new ExecutionSet
        {
            ToExecute = [.. toExecute.OrderBy(id => id.Value, StringComparer.Ordinal)],
            Skipped = [.. skipped.OrderBy(entry => entry.RuleId.Value, StringComparer.Ordinal)],
        };
    }
}
