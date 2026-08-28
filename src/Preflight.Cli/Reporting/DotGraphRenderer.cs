namespace Preflight.Cli.Reporting;

using System.Globalization;
using System.Text;
using Preflight.Abstractions.Rules;
using Preflight.Core;

/// <summary>
/// Renders the rule dependency graph as Graphviz DOT, for
/// <c>graph --format dot</c>.
/// </summary>
/// <remarks>
/// The point of <c>graph</c> is being diffable, so the node order is the output
/// rather than a detail of it: topological level ascending, ordinal within the
/// level. Edges run from dependency to dependent so the picture reads in
/// execution order, and one <c>rank=same</c> group per level keeps the levels
/// the design argues for from being scattered by the layout engine.
/// </remarks>
public sealed class DotGraphRenderer
{
    private readonly TextWriter _output;

    public DotGraphRenderer(TextWriter output)
    {
        _output = output;
    }

    /// <summary>
    /// Writes the whole digraph.
    /// </summary>
    /// <remarks>
    /// <c>rankdir=LR</c> and nothing else. Colouring by stage, or by enabled
    /// and disabled, is the obvious embellishment and it is refused:
    /// <c>graph</c> resolves no policy at all, so there is no disabled state to
    /// paint and a colour by stage would be decoration that has to be kept in
    /// step with an enum.
    /// </remarks>
    public void Render(RuleGraph graph, IReadOnlyList<RuleDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(descriptors);

        var dependencies = descriptors.ToDictionary(
            descriptor => descriptor.Id,
            descriptor => descriptor.DependsOn);

        var writer = new StringBuilder("digraph preflight {\n")
            .Append("  rankdir=LR;\n");

        foreach (var level in graph.Levels)
        {
            writer.Append("  { rank=same;");

            foreach (var id in level)
            {
                writer.Append(CultureInfo.InvariantCulture, $" {Quote(id)};");
            }

            writer.Append(" }\n");
        }

        WriteEdges(writer, graph, dependencies);

        writer.Append("}\n");

        _output.Write(writer.ToString());
    }

    /// <remarks>
    /// Dependency to dependent, so the arrow points the way execution goes.
    /// Reversed, the file is still valid DOT, still renders, and says the
    /// opposite of what the graph means.
    ///
    /// Nodes in presentation order and dependencies ordinally within a node,
    /// for the reason every other output is fixed: the file is read by diffing
    /// it against the last one, and an order that follows how the descriptors
    /// were written down makes a rule moved in a source file look like a
    /// changed graph.
    /// </remarks>
    private static void WriteEdges(
        StringBuilder writer,
        RuleGraph graph,
        IReadOnlyDictionary<RuleId, IReadOnlyList<RuleId>> dependencies)
    {
        var edges = graph.Levels
            .SelectMany(level => level)
            .SelectMany(id => dependencies
                .GetValueOrDefault(id, [])
                .Order(RuleIdComparer.Ordinal)
                .Select(dependency => (dependency, id)))
            .ToArray();

        if (edges.Length == 0)
        {
            return;
        }

        writer.Append('\n');

        foreach (var (dependency, dependent) in edges)
        {
            writer.Append(CultureInfo.InvariantCulture, $"  {Quote(dependency)} -> {Quote(dependent)};\n");
        }
    }

    /// <remarks>
    /// Always quoted. Neither <c>.</c> nor <c>-</c> is legal in a bare DOT
    /// identifier and every rule id contains at least one of them, so an
    /// unquoted node makes Graphviz refuse the whole file.
    /// </remarks>
    private static string Quote(RuleId id) => $"\"{id.Value}\"";

    /// <summary>
    /// Orders rule ids the way every other output does, by value and ordinally.
    /// </summary>
    private sealed class RuleIdComparer : IComparer<RuleId>
    {
        public static RuleIdComparer Ordinal { get; } = new();

        public int Compare(RuleId x, RuleId y) =>
            string.CompareOrdinal(x.Value, y.Value);
    }
}
