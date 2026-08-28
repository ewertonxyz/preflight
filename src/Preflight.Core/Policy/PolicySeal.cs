namespace Preflight.Core.Policy;

/// <summary>
/// Which file sealed a path, and with which pattern.
/// </summary>
/// <remarks>
/// No line, and that is a decision rather than an omission.
/// <c>PolicyDocument.ReadArray</c> collapses an array into a single leaf, so
/// every entry of one <c>sealed</c> block would share one number — twenty seals
/// pointing at the same place. The pattern is what tells the reader which one
/// they hit, and the error already carries the line of the file that violated
/// it, which is the line somebody has to edit. See ADR-031.
/// </remarks>
/// <param name="FilePath">The file that declared the seal.</param>
/// <param name="Pattern">The entry as it was written.</param>
public sealed record SealSource(string FilePath, string Pattern);

/// <summary>
/// One entry of a <c>sealed</c> array.
/// </summary>
/// <remarks>
/// <para>
/// <c><![CDATA[<rule-id-pattern>:<key-path>]]></c>. The separator is <c>:</c>
/// and not <c>.</c>, for the reason <c>Docs/design.md 6.2</c> gives about
/// <c>--set</c>: a rule id contains dots, so a fully dotted path cannot be
/// split back into an id and a key.
/// </para>
/// <para>
/// Three shapes, and each earns its place: <c>core.presubmit.large-file:settings.maxBytes</c>
/// reaches into <c>settings</c>, which is the headline use — a project that may
/// not raise a limit; <c>:cachePath</c> has an empty id and seals a root key,
/// exactly as <c>--set :maxDegreeOfParallelism=4</c> already writes one; and
/// <c>security.*:enabled</c> ends the id in a wildcard. See ADR-031.
/// </para>
/// </remarks>
public readonly record struct SealPattern(string RuleIdPattern, string KeyPath)
{
    private const char Separator = ':';

    private const char Wildcard = '*';

    /// <summary>Whether this pattern seals a root key rather than a rule's.</summary>
    public bool IsRootKey => RuleIdPattern.Length == 0;

    public static bool TryParse(string text, out SealPattern pattern)
    {
        pattern = default;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var parts = text.Split(Separator);

        if (parts.Length != 2)
        {
            return false;
        }

        var (id, key) = (parts[0], parts[1]);

        // The wildcard ends the id or does not appear. In the middle it would
        // be a glob — a pattern language to write and test — and on the right
        // of the separator it would seal a rule's whole shape, collapsing
        // blocking and gating, which section 7.2 keeps apart on purpose.
        if (id.Contains(Wildcard) && !id.EndsWith(Wildcard))
        {
            return false;
        }

        if (key.Length == 0 || key.Contains(Wildcard) || !IsKeyPath(key))
        {
            return false;
        }

        pattern = new SealPattern(id, key);

        return true;
    }

    private static bool IsKeyPath(string key) =>
        key.Split('.').All(segment =>
            segment.Length > 0 &&
            segment.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));

    /// <summary>
    /// Whether this pattern covers one rule's key.
    /// </summary>
    /// <param name="ruleId">The rule, or <see langword="null"/> for a root key.</param>
    /// <param name="keyPath">The dotted key path inside that scope.</param>
    public bool Covers(string? ruleId, string keyPath)
    {
        if (!string.Equals(KeyPath, keyPath, StringComparison.Ordinal))
        {
            return false;
        }

        if (IsRootKey)
        {
            return ruleId is null;
        }

        if (ruleId is null)
        {
            return false;
        }

        return RuleIdPattern.EndsWith(Wildcard)
            ? ruleId.StartsWith(RuleIdPattern[..^1], StringComparison.Ordinal)
            : string.Equals(RuleIdPattern, ruleId, StringComparison.Ordinal);
    }
}

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
