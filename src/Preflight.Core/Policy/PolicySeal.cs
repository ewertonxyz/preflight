namespace Preflight.Core.Policy;

/// <summary>
/// Every path the policy chain declared unchangeable, and who declared it.
/// </summary>
/// <remarks>
/// <para>
/// <c>Docs/design.md 6.3</c> names the hole this closes: nothing stopped a
/// downstream layer setting <c>"blocking": false</c> on a rule the studio
/// requires, and the run went green having checked less than the policy asked
/// for.
/// </para>
/// <para>
/// Seals are <b>unioned</b> along the <c>extends</c> chain and never replaced.
/// A descendant cannot remove an ancestor's seal, which is why this is built
/// from the individual documents rather than from the merged one — merging an
/// array keeps the stronger side whole, so a pipeline declaring its own
/// <c>sealed</c> would erase the baseline's without a word. See ADR-031.
/// </para>
/// </remarks>
public sealed class PolicySeal
{
    /// <summary>The root key that holds the patterns.</summary>
    public const string KeyName = "sealed";

    private readonly IReadOnlyList<(SealPattern Pattern, SealSource Source, int ChainIndex)> _seals;

    private readonly IReadOnlyDictionary<string, int> _chainIndexOf;

    private PolicySeal(
        IReadOnlyList<(SealPattern, SealSource, int)> seals,
        IReadOnlyDictionary<string, int> chainIndexOf)
    {
        _seals = seals;
        _chainIndexOf = chainIndexOf;
    }

    /// <summary>Nothing is sealed.</summary>
    public static PolicySeal None { get; } =
        new([], new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

    public bool IsEmpty => _seals.Count == 0;

    /// <summary>
    /// Reads the seals declared by a chain, ancestors first.
    /// </summary>
    /// <remarks>
    /// Entries that do not parse are skipped rather than raised:
    /// <see cref="PolicyValidator"/> walks the same array and reports them, and
    /// raising here would mean the same malformed pattern produced two errors
    /// or one, depending on which caller got there first.
    /// </remarks>
    public static PolicySeal Parse(IReadOnlyList<PolicyDocument> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);

        var seals = new List<(SealPattern, SealSource, int)>();
        var indexOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < chain.Count; index++)
        {
            var document = chain[index];

            indexOf[document.FilePath] = index;

            if (document.Root is not PolicyNode.ObjectNode root ||
                root.Members.GetValueOrDefault(KeyName) is not PolicyNode.Leaf leaf ||
                leaf.Value.Value is not string[] entries)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                // First declaration wins the attribution. The ancestor is the
                // one whose authority is being exercised, and a descendant
                // repeating the same pattern adds nothing but noise to the
                // message somebody will read.
                if (!SealPattern.TryParse(entry, out var pattern) || !claimed.Add(entry))
                {
                    continue;
                }

                seals.Add((pattern, new SealSource(document.FilePath, entry), index));
            }
        }

        return seals.Count == 0 ? None : new PolicySeal(seals, indexOf);
    }

    /// <summary>
    /// Whether a path is sealed, and by which file.
    /// </summary>
    /// <param name="ruleId">The rule, or <see langword="null"/> for a root key.</param>
    /// <param name="keyPath">The dotted key path inside that scope.</param>
    /// <param name="declaredBy">Where the seal came from.</param>
    /// <param name="afterFilePath">
    /// Only seals declared strictly before this file in the chain apply. A file
    /// does not seal against itself, and neither does its ancestor's copy of
    /// its own values. <see langword="null"/> means every seal applies, which
    /// is what the layers below the chain — targets, the local overlay,
    /// <c>--set</c> — are subject to.
    /// </param>
    public bool IsSealed(
        string? ruleId,
        string keyPath,
        out SealSource declaredBy,
        string? afterFilePath = null)
    {
        // A file not in the chain sits below all of it: that is the local
        // overlay, a targets block and --set, none of which can declare a seal
        // and all of which are bound by every one.
        var ceiling = afterFilePath is not null && _chainIndexOf.TryGetValue(afterFilePath, out var index)
            ? index
            : int.MaxValue;

        foreach (var (pattern, source, chainIndex) in _seals)
        {
            if (chainIndex < ceiling && pattern.Covers(ruleId, keyPath))
            {
                declaredBy = source;

                return true;
            }
        }

        declaredBy = null!;

        return false;
    }
}
