namespace Preflight.Core.Policy;

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
