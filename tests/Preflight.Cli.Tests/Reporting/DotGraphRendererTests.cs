namespace Preflight.Cli.Tests.Reporting;

using System.Text.RegularExpressions;
using Preflight.Abstractions.Rules;
using Preflight.Cli.Reporting;
using Preflight.Core;
using Preflight.TestSupport;

/// <summary>
/// Fixes the Graphviz DOT rendering of <c>graph --format dot</c>.
/// </summary>
/// <remarks>
/// The rule graph says the point of this command is being diffable, so the node
/// order is the output rather than a detail of it. Everything below is either
/// that ordering or something that would leave the file syntactically wrong
/// while every containment assertion stayed green.
/// </remarks>
public sealed class DotGraphRendererTests
{
    /// <summary>
    /// Three levels, and a level with two nodes in it.
    /// </summary>
    /// <remarks>
    /// A diamond rather than a chain, because a chain has one node per level and
    /// cannot tell an ordering by level apart from an ordering by insertion.
    /// </remarks>
    private static readonly string[] DiamondIds =
        ["core.a.top", "core.a.left", "core.a.right", "core.a.bottom"];

    private static RuleDescriptor[] Diamond() =>
    [
        RuleDescriptorFixture.Rule("core.a.top"),
        RuleDescriptorFixture.Rule("core.a.left", "core.a.top"),
        RuleDescriptorFixture.Rule("core.a.right", "core.a.top"),
        RuleDescriptorFixture.Rule("core.a.bottom", "core.a.left", "core.a.right"),
    ];

    private static string Render(params RuleDescriptor[] descriptors)
    {
        var output = new StringWriter();

        new DotGraphRenderer(output).Render(RuleGraph.Build(descriptors), descriptors);

        return output.ToString();
    }

    [Fact]
    public void Render_OrdersNodesByLevelThenOrdinalWithinTheLevel()
    {
        var rendered = Render(Diamond());

        var positions = DiamondIds
            .Select(id => rendered.IndexOf(id, StringComparison.Ordinal))
            .ToArray();

        positions.ShouldAllBe(position => position >= 0);
        positions.ShouldBe(positions.Order().ToArray());
    }

    /// <remarks>
    /// The level is the assertion the rule graph makes about the graph, and a
    /// layout engine left to itself scatters the nodes of one level across the
    /// picture — erasing the only thing the drawing exists to show.
    /// </remarks>
    [Fact]
    public void Render_EmitsOneRankSameGroupPerTopologicalLevel()
    {
        var rendered = Render(Diamond());

        Regex.Count(rendered, "rank=same").ShouldBe(3);

        var middle = rendered
            .Split('\n')
            .Single(line => line.Contains("rank=same", StringComparison.Ordinal) &&
                line.Contains("core.a.left", StringComparison.Ordinal));

        middle.ShouldContain("core.a.right");
    }

    /// <remarks>
    /// Reversing the arrow draws the graph against the order execution happens
    /// in, and produces a DOT file that is still valid, still renders, and says
    /// the opposite. Nothing else in the suite would notice.
    /// </remarks>
    [Fact]
    public void Render_EmitsEdgesFromDependencyToDependent()
    {
        var rendered = Render(Diamond());

        rendered.ShouldContain("\"core.a.top\" -> \"core.a.left\"");
        rendered.ShouldNotContain("\"core.a.left\" -> \"core.a.top\"");
    }

    /// <summary>
    /// Node identifiers are quoted.
    /// </summary>
    /// <remarks>
    /// A real output defect, and one a containment check cannot see. Neither
    /// <c>.</c> nor <c>-</c> is valid in a bare DOT identifier, so an unquoted
    /// rule id makes Graphviz refuse the whole file — while
    /// <c>ShouldContain("core.build.compile-probe")</c> passes either way.
    /// </remarks>
    [Fact]
    public void Render_QuotesNodeIdentifiers()
    {
        var rendered = Render(RuleDescriptorFixture.Rule("core.build.compile-probe"));

        rendered.ShouldContain("\"core.build.compile-probe\"");
        rendered.ShouldNotMatch("(?<![\"\\w.-])core\\.build\\.compile-probe(?![\"\\w.-])");
    }

    /// <remarks>
    /// The boundary where an emitter of <c>rank=same</c> produces an empty group
    /// or an orphan arrow: every node is a root, so there is exactly one level
    /// and no edge at all.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Render_ForAGraphWithoutEdges_EmitsEveryNodeAndNoEdge(int count)
    {
        var descriptors = Enumerable
            .Range(0, count)
            .Select(index => RuleDescriptorFixture.Rule($"core.roots.n{index}"))
            .ToArray();

        var rendered = Render(descriptors);

        rendered.Split('\n').Count(line => line.Contains("->", StringComparison.Ordinal)).ShouldBe(0);
        Regex.Count(rendered, "rank=same").ShouldBe(1);

        var group = rendered
            .Split('\n')
            .Single(line => line.Contains("rank=same", StringComparison.Ordinal));

        foreach (var descriptor in descriptors)
        {
            rendered.ShouldContain($"\"{descriptor.Id.Value}\"");
            group.ShouldContain(descriptor.Id.Value);
        }
    }

    /// <remarks>
    /// The rule graph makes this command's output diffable, and the same guard the
    /// other reporters carry applies for the same reason.
    /// </remarks>
    [Fact]
    public void Render_RepeatedOverTheSameGraph_ProducesIdenticalBytes()
    {
        var descriptors = Diamond();
        var first = Render(descriptors);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            Render(descriptors).ShouldBe(first);
        }
    }

    /// <remarks>
    /// Indentation, quoting, <c>rankdir</c> and the order of the groups: the
    /// correctness of a DOT file is its exact bytes, because the next thing to
    /// read it is a layout engine and not a person.
    /// </remarks>
    [Fact]
    public Task Render_ForTheDocumentedGraph_MatchesTheGolden() => Verify(Render(Diamond()));
}
