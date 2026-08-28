namespace Preflight.Core;

using Preflight.Abstractions;
using Preflight.Core.Policy;

/// <summary>
/// The dependency graph of the discovered rules, levelled topologically.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately knows nothing about stages or policy: an edge is an edge. Which
/// rules seed a run and which are disabled are questions
/// <see cref="ExecutionSet"/> answers on top of this, and keeping them out is
/// what makes the dependency closure expressible at all.
/// </para>
/// <para>
/// <see cref="Build"/> is always given the full discovered universe: every
/// stage, enabled and disabled alike. Filtering before this point would make a
/// dependency on a disabled rule indistinguishable from a dependency on a
/// misspelled one — both would simply be "not in the set I was given" — and 4.4
/// needs those to be different outcomes. It also means a cycle anywhere in the
/// rule set is found on every run, not only on the run whose stage happens to
/// reach it.
/// </para>
/// </remarks>
public sealed class RuleGraph
{
    private readonly IReadOnlyDictionary<RuleId, IReadOnlyList<RuleId>> _dependencies;
    private readonly IReadOnlyDictionary<RuleId, IReadOnlyList<RuleId>> _dependents;

    private RuleGraph(
        IReadOnlyList<IReadOnlyList<RuleId>> levels,
        IReadOnlyDictionary<RuleId, IReadOnlyList<RuleId>> dependencies,
        IReadOnlyDictionary<RuleId, IReadOnlyList<RuleId>> dependents)
    {
        Levels = levels;
        _dependencies = dependencies;
        _dependents = dependents;
    }

    /// <summary>
    /// Topological levels, dependencies first. Within a level, ids are ordered
    /// ordinally so that <c>preflight graph</c> is diffable between two runs.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<RuleId>> Levels { get; }

    public static RuleGraph Build(IReadOnlyList<RuleDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var errors = new List<GraphValidationError>();
        var byId = IndexById(descriptors, errors);

        var dependencies = ResolveDependencies(descriptors, byId, errors);
        var dependents = Invert(dependencies);
        var levels = Levelise(dependencies, errors);

        if (errors.Count > 0)
        {
            throw new GraphValidationException(errors);
        }

        return new RuleGraph(levels, Freeze(dependencies), Freeze(dependents));
    }

    /// <summary>
    /// Every rule this one depends on, directly or transitively, in ordinal
    /// order.
    /// </summary>
    public IReadOnlyList<RuleId> TransitiveDependenciesOf(RuleId id) => Reachable(id, _dependencies);

    /// <summary>
    /// Every rule that depends on this one, directly or transitively, in
    /// ordinal order. This is what the skip propagation of 7.3 walks.
    /// </summary>
    public IReadOnlyList<RuleId> TransitiveDependentsOf(RuleId id) => Reachable(id, _dependents);

    private static Dictionary<RuleId, RuleDescriptor> IndexById(
        IReadOnlyList<RuleDescriptor> descriptors, List<GraphValidationError> errors)
    {
        var byId = new Dictionary<RuleId, RuleDescriptor>();

        foreach (var descriptor in descriptors)
        {
            if (!byId.TryAdd(descriptor.Id, descriptor))
            {
                errors.Add(new GraphValidationError.DuplicateRuleId(descriptor.Id));
            }
        }

        return byId;
    }

    /// <remarks>
    /// A dangling edge — one naming an id nobody declares — is recorded and
    /// then dropped, so that levelling still runs and a cycle elsewhere in the
    /// same descriptor set is reported in the same pass. Leaving it in would
    /// hold its node at non-zero in-degree forever and turn a typo into a
    /// phantom cycle.
    /// </remarks>
    private static Dictionary<RuleId, List<RuleId>> ResolveDependencies(
        IReadOnlyList<RuleDescriptor> descriptors,
        Dictionary<RuleId, RuleDescriptor> byId,
        List<GraphValidationError> errors)
    {
        var knownIds = byId.Keys.Select(id => id.Value).ToArray();
        var dependencies = byId.Keys.ToDictionary(id => id, _ => new List<RuleId>());

        foreach (var descriptor in descriptors)
        {
            if (!dependencies.TryGetValue(descriptor.Id, out var edges))
            {
                continue;
            }

            foreach (var dependency in descriptor.DependsOn)
            {
                if (dependency == descriptor.Id)
                {
                    errors.Add(new GraphValidationError.SelfDependency(descriptor.Id));
                    continue;
                }

                if (!byId.ContainsKey(dependency))
                {
                    errors.Add(new GraphValidationError.MissingDependency(
                        descriptor.Id, dependency, SuggestionFinder.FindClosest(dependency.Value, knownIds)));
                    continue;
                }

                if (!edges.Contains(dependency))
                {
                    edges.Add(dependency);
                }
            }
        }

        return dependencies;
    }

    private static Dictionary<RuleId, List<RuleId>> Invert(Dictionary<RuleId, List<RuleId>> dependencies)
    {
        var dependents = dependencies.Keys.ToDictionary(id => id, _ => new List<RuleId>());

        foreach (var (id, edges) in dependencies)
        {
            foreach (var dependency in edges)
            {
                dependents[dependency].Add(id);
            }
        }

        return dependents;
    }

    /// <remarks>
    /// Kahn's algorithm, one round per level. Nodes whose dependencies are all
    /// placed become the next level; ids inside a level are sorted ordinally,
    /// which is the only thing making the output stable across runs. Whatever
    /// is left when no round can advance is exactly the set involved in a
    /// cycle.
    /// </remarks>
    private static List<IReadOnlyList<RuleId>> Levelise(
        Dictionary<RuleId, List<RuleId>> dependencies, List<GraphValidationError> errors)
    {
        var remaining = new Dictionary<RuleId, List<RuleId>>(dependencies);
        var placed = new HashSet<RuleId>();
        var levels = new List<IReadOnlyList<RuleId>>();

        while (remaining.Count > 0)
        {
            var level = remaining
                .Where(entry => entry.Value.All(placed.Contains))
                .Select(entry => entry.Key)
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();

            if (level.Length == 0)
            {
                errors.Add(new GraphValidationError.CycleDetected(FindCycle(remaining)));
                break;
            }

            foreach (var id in level)
            {
                remaining.Remove(id);
                placed.Add(id);
            }

            levels.Add(level);
        }

        return levels;
    }

    /// <remarks>
    /// Walks dependency edges from the ordinally-first surviving node until it
    /// revisits one, then returns the loop from that revisit onward. Kahn only
    /// tells you <em>which</em> nodes are stuck, never in what order they reach
    /// each other — reconstructing the path is the second pass that 7.1's
    /// "print the path, in order" requirement actually costs.
    /// </remarks>
    private static IReadOnlyList<RuleId> FindCycle(Dictionary<RuleId, List<RuleId>> remaining)
    {
        var start = remaining.Keys.OrderBy(id => id.Value, StringComparer.Ordinal).First();
        var path = new List<RuleId>();
        var seen = new HashSet<RuleId>();
        var current = start;

        while (seen.Add(current))
        {
            path.Add(current);
            current = remaining[current].First(remaining.ContainsKey);
        }

        var loopStart = path.IndexOf(current);

        return [.. path[loopStart..], current];
    }

    private static IReadOnlyList<RuleId> Reachable(
        RuleId id, IReadOnlyDictionary<RuleId, IReadOnlyList<RuleId>> edges)
    {
        if (!edges.ContainsKey(id))
        {
            throw new KeyNotFoundException(
                $"Rule '{id}' is not part of this graph. An empty result would be indistinguishable from a rule that " +
                "genuinely has none.");
        }

        // An explicit stack rather than recursion: a rule set deep enough to
        // exhaust the call stack would take the whole process down with a
        // StackOverflowException, which .NET does not let anyone catch or
        // report.
        var found = new HashSet<RuleId>();
        var pending = new Stack<RuleId>();
        pending.Push(id);

        while (pending.TryPop(out var current))
        {
            foreach (var next in edges[current])
            {
                if (found.Add(next))
                {
                    pending.Push(next);
                }
            }
        }

        return [.. found.OrderBy(found => found.Value, StringComparer.Ordinal)];
    }

    private static Dictionary<RuleId, IReadOnlyList<RuleId>> Freeze(
        Dictionary<RuleId, List<RuleId>> edges) =>
        edges.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<RuleId>)[.. entry.Value.OrderBy(id => id.Value, StringComparer.Ordinal)]);
}
