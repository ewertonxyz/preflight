namespace Preflight.Rules;

/// <summary>
/// One <c>when this changes, that must exist</c> pair, parsed from policy.
/// </summary>
/// <remarks>
/// <para>
/// A string with an arrow rather than an object with two fields, because policy
/// settings carry homogeneous arrays of scalars today and an object inside an
/// array would need the parser, the policy node and the scoped reader all to
/// learn a shape none of them has — a second contract change, to express what
/// an arrow already expresses.
/// </para>
/// <para>
/// Parsed once, before the loop over changed files, so that a malformed entry
/// is reported once against the policy rather than once per file that happened
/// to match nothing.
/// </para>
/// </remarks>
internal sealed record CompanionRequirement
{
    /// <summary>
    /// What separates the pattern from the template.
    /// </summary>
    public const string Arrow = "->";

    private CompanionRequirement(string text, GlobPattern when, string template)
    {
        Text = text;
        When = when;
        Template = template;
    }

    /// <summary>
    /// The entry as the policy wrote it, for a report that has to name it.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// The paths this requirement applies to.
    /// </summary>
    public GlobPattern When { get; }

    /// <summary>
    /// The companion's path, with tokens still in it.
    /// </summary>
    public string Template { get; }

    /// <summary>
    /// Reads one entry.
    /// </summary>
    /// <param name="entry">The entry as the policy wrote it.</param>
    /// <returns>
    /// The requirement, or <see langword="null"/> when the entry has no arrow
    /// or either side of it is empty.
    /// </returns>
    /// <remarks>
    /// Null rather than an exception, because a policy typo is something the
    /// reader has to be told about in a finding that names the entry — not an
    /// errored rule that names a stack.
    /// </remarks>
    public static CompanionRequirement? Parse(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var arrow = entry.IndexOf(Arrow, StringComparison.Ordinal);

        if (arrow < 0)
        {
            return null;
        }

        var pattern = entry[..arrow].Trim();
        var template = entry[(arrow + Arrow.Length)..].Trim();

        return pattern.Length == 0 || template.Length == 0
            ? null
            : new CompanionRequirement(entry, GlobPattern.Compile(pattern), template);
    }

    /// <summary>
    /// The companion's path for one matched file.
    /// </summary>
    /// <param name="relativePath">
    /// The matched path, relative to the workspace root, with forward slashes.
    /// </param>
    /// <returns>The companion's path, relative to the workspace root.</returns>
    /// <remarks>
    /// <c>{dir}</c> is the directory and is empty at the workspace root, where
    /// the join emits no separator — otherwise every pair written for a nested
    /// asset would produce a leading slash the moment somebody put one at the
    /// top of the repository. <c>{name}</c> is the file name without its
    /// extension and <c>{ext}</c> is the extension without its dot, so that a
    /// template can put the dot where it wants one.
    /// </remarks>
    public string Resolve(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var slash = relativePath.LastIndexOf('/');
        var directory = slash >= 0 ? relativePath[..slash] : string.Empty;
        var fileName = slash >= 0 ? relativePath[(slash + 1)..] : relativePath;

        // The last dot, unless it is the first character. A dotfile's whole name
        // is its name — the BCL splits ".gitignore" into an empty name and an
        // extension, which would resolve a companion for it to a file called
        // nothing.
        var dot = fileName.LastIndexOf('.');
        var name = dot > 0 ? fileName[..dot] : fileName;
        var extension = dot > 0 ? fileName[(dot + 1)..] : string.Empty;

        var resolved = Template
            .Replace("{dir}", directory, StringComparison.Ordinal)
            .Replace("{name}", name, StringComparison.Ordinal)
            .Replace("{ext}", extension, StringComparison.Ordinal);

        // An empty {dir} leaves the separator the template wrote behind it, and
        // a path beginning with one is rooted rather than relative — which is a
        // different file, on a different part of the disk.
        return resolved.StartsWith('/') ? resolved[1..] : resolved;
    }
}
