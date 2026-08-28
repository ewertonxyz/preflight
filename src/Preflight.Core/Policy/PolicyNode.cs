namespace Preflight.Core.Policy;

/// <summary>
/// A JSON-shaped tree of policy values, each leaf carrying its own
/// <see cref="PolicyValue{T}"/> history.
/// </summary>
/// <remarks>
/// Merging is per key and never per object, at any depth.
/// <see cref="Merge"/> is the single recursive primitive that requirement is
/// built on, and it is reused for three distinct purposes: the <c>extends</c>
/// chain (<c>PolicyLoader</c>), the named-layer precedence chain
/// (<c>EffectivePolicy</c>), and applying an already-typed <c>--set</c> overlay
/// (<c>PolicySetOverride</c>). Keeping one implementation is what prevents the
/// JSON-layer merge and the <c>--set</c> merge from silently diverging.
/// </remarks>
public abstract record PolicyNode
{
    private PolicyNode()
    {
    }

    public sealed record Leaf(PolicyValue<object?> Value) : PolicyNode;

    public sealed record ObjectNode(IReadOnlyDictionary<string, PolicyNode> Members) : PolicyNode;

    /// <summary>
    /// Merges <paramref name="stronger"/> over <paramref name="weaker"/>.
    /// </summary>
    /// <remarks>
    /// A present key always overrides, including an explicit JSON
    /// <see langword="null"/> leaf — presence decides, not the value. A key
    /// that <paramref name="stronger"/> never mentions falls through untouched,
    /// keeping <paramref name="weaker"/>'s value <em>and</em> its history. When
    /// the two sides disagree in shape at the same path (an object replaced by
    /// a scalar or vice versa), the stronger side replaces the whole subtree —
    /// there is no meaningful partial merge between an object and a scalar.
    /// </remarks>
    public static PolicyNode Merge(PolicyNode weaker, PolicyNode stronger)
    {
        if (weaker is ObjectNode weakerObject && stronger is ObjectNode strongerObject)
        {
            var merged = new Dictionary<string, PolicyNode>(weakerObject.Members);

            foreach (var (key, strongerChild) in strongerObject.Members)
            {
                merged[key] = merged.TryGetValue(key, out var weakerChild)
                    ? Merge(weakerChild, strongerChild)
                    : strongerChild;
            }

            return new ObjectNode(merged);
        }

        if (weaker is Leaf weakerLeaf && stronger is Leaf strongerLeaf)
        {
            return new Leaf(new PolicyValue<object?>
            {
                Entries = [.. weakerLeaf.Value.Entries, .. strongerLeaf.Value.Entries],
            });
        }

        return stronger;
    }

    /// <summary>
    /// Navigates a dot-separated path. Only safe when no segment of the path
    /// can itself contain a literal dot — a <c>RuleId</c> always does so any
    /// path that crosses a rule id must use the segment-list overload instead,
    /// with the rule id passed as one pre-split segment.
    /// </summary>
    public bool TryGetPath(string path, out PolicyNode? result) => TryGetPath(path.Split('.'), out result);

    public bool TryGetPath(IReadOnlyList<string> segments, out PolicyNode? result)
    {
        PolicyNode current = this;

        foreach (var segment in segments)
        {
            if (current is not ObjectNode obj || !obj.Members.TryGetValue(segment, out var next))
            {
                result = null;
                return false;
            }

            current = next;
        }

        result = current;
        return true;
    }
}
