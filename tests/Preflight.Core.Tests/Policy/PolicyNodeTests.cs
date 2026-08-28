namespace Preflight.Core.Tests.Policy;

using Preflight.Core.Policy;

/// <summary>
/// Fixes <see cref="PolicyNode.Merge"/>, the single recursive by-key merge
/// primitive reused for <c>extends</c> resolution, named-layer precedence, and
/// applying a <c>--set</c> overlay.
/// </summary>
/// <remarks>
/// Merging is per key and never per object, at any depth. A shallow
/// implementation passes every test where
/// colliding keys live at different parents; it only fails when two layers
/// touch the same nested parent at different children, which is exactly what
/// the depth-2/3 cases below force.
/// </remarks>
public sealed class PolicyNodeTests
{
    private static readonly PolicyOrigin WeakerOrigin = new PolicyOrigin.FromFile("base.json", 1);
    private static readonly PolicyOrigin StrongerOrigin = new PolicyOrigin.FromFile("atlas.json", 1);

    [Fact]
    public void Merge_WhenStrongerOverridesOneLeafKey_PreservesSiblingKeys()
    {
        var weaker = Obj(new()
        {
            ["core.presubmit.large-file"] = Obj(new() { ["settings"] = Obj(new() { ["maxBytes"] = Leaf(5242880L, WeakerOrigin) }) }),
            ["core.workspace.toolchain"] = Obj(new() { ["enabled"] = Leaf(true, WeakerOrigin) }),
        });

        var stronger = Obj(new()
        {
            ["core.presubmit.large-file"] = Obj(new() { ["settings"] = Obj(new() { ["maxBytes"] = Leaf(52428800L, StrongerOrigin) }) }),
        });

        var merged = PolicyNode.Merge(weaker, stronger);

        ValueAt(merged, ["core.presubmit.large-file", "settings", "maxBytes"]).ShouldBe(52428800L);
        ValueAt(merged, ["core.workspace.toolchain", "enabled"]).ShouldBe(true);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Merge_AtArbitraryDepth_OverridesOnlyTheTargetedNestedKey(int depth)
    {
        var path = string.Join('.', Enumerable.Range(0, depth).Select(i => $"level{i}"));

        var weaker = Obj(new() { ["settings"] = NestedLeaf(path, "sibling", 1L, WeakerOrigin) });
        var stronger = Obj(new() { ["settings"] = NestedLeaf(path, "target", 2L, StrongerOrigin) });

        var merged = PolicyNode.Merge(weaker, stronger);

        ValueAt(merged, $"settings.{path}.sibling").ShouldBe(1L);
        ValueAt(merged, $"settings.{path}.target").ShouldBe(2L);
    }

    [Fact]
    public void Merge_WhenStrongerSetsExplicitNull_OverridesWithNullRatherThanFallingThrough()
    {
        var weaker = Obj(new() { ["settings"] = Obj(new() { ["foo"] = Leaf(1L, WeakerOrigin) }) });
        var stronger = Obj(new() { ["settings"] = Obj(new() { ["foo"] = Leaf(null, StrongerOrigin) }) });

        var merged = PolicyNode.Merge(weaker, stronger);

        var leaf = (PolicyNode.Leaf)Navigate(merged, "settings.foo");

        leaf.Value.Value.ShouldBeNull();
        leaf.Value.Origin.ShouldBe(StrongerOrigin);
    }

    [Fact]
    public void Merge_WhenStrongerDoesNotMentionAKeyAtAll_KeepsWeakersValueAndOrigin()
    {
        var weaker = Obj(new() { ["settings"] = Obj(new() { ["foo"] = Leaf(1L, WeakerOrigin) }) });
        var stronger = Obj(new() { ["settings"] = Obj(new() { }) });

        var merged = PolicyNode.Merge(weaker, stronger);

        var leaf = (PolicyNode.Leaf)Navigate(merged, "settings.foo");

        leaf.Value.Value.ShouldBe(1L);
        leaf.Value.Origin.ShouldBe(WeakerOrigin);
    }

    [Fact]
    public void Merge_WhenStrongerReplacesAnObjectWithAScalar_ReplacesTheWholeSubtree()
    {
        var weaker = Obj(new() { ["settings"] = Obj(new() { ["limits"] = Obj(new() { ["maxBytes"] = Leaf(5L, WeakerOrigin), ["unit"] = Leaf("b", WeakerOrigin) }) }) });
        var stronger = Obj(new() { ["settings"] = Obj(new() { ["limits"] = Leaf(0L, StrongerOrigin) }) });

        var merged = PolicyNode.Merge(weaker, stronger);

        ValueAt(merged, "settings.limits").ShouldBe(0L);
    }

    [Fact]
    public void Merge_UnderSettings_NeverValidatesUnknownKeys()
    {
        var weaker = Obj(new() { ["settings"] = Obj(new() { }) });
        var stronger = Obj(new() { ["settings"] = Obj(new() { ["whateverTheRuleWants"] = Leaf("opaque", StrongerOrigin) }) });

        var merged = PolicyNode.Merge(weaker, stronger);

        ValueAt(merged, "settings.whateverTheRuleWants").ShouldBe("opaque");
    }

    private static PolicyNode.Leaf Leaf(object? value, PolicyOrigin origin) =>
        new(PolicyValue.Initial(value, origin));

    private static PolicyNode.ObjectNode Obj(Dictionary<string, PolicyNode> members) => new(members);

    private static PolicyNode NestedLeaf(string path, string finalKey, object? value, PolicyOrigin origin)
    {
        PolicyNode node = Obj(new() { [finalKey] = Leaf(value, origin) });

        foreach (var segment in path.Split('.').Reverse())
        {
            node = Obj(new() { [segment] = node });
        }

        return node;
    }

    private static PolicyNode Navigate(PolicyNode root, string path)
    {
        root.TryGetPath(path, out var result).ShouldBeTrue($"Expected path '{path}' to exist.");
        return result!;
    }

    private static PolicyNode Navigate(PolicyNode root, IReadOnlyList<string> segments)
    {
        root.TryGetPath(segments, out var result).ShouldBeTrue($"Expected path '{string.Join('.', segments)}' to exist.");
        return result!;
    }

    private static object? ValueAt(PolicyNode root, string path) =>
        ((PolicyNode.Leaf)Navigate(root, path)).Value.Value;

    private static object? ValueAt(PolicyNode root, IReadOnlyList<string> segments) =>
        ((PolicyNode.Leaf)Navigate(root, segments)).Value.Value;
}
