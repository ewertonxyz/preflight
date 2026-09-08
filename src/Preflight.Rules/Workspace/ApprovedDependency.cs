namespace Preflight.Rules;

/// <summary>
/// One entry of the approved list, written <c>id@version</c>.
/// </summary>
/// <remarks>
/// <para>
/// Parsed once, before the loop over declared dependencies, so that a malformed
/// entry is reported against the policy that holds it rather than against a
/// dependency that happens to be compared with it.
/// </para>
/// <para>
/// Split on the <em>last</em> <c>@</c>, because a scoped package name begins
/// with one: <c>@scope/pkg@1.0.0</c> has to read as that package at that
/// version, and splitting on the first would produce an entry with an empty id.
/// </para>
/// </remarks>
internal sealed record ApprovedDependency
{
    /// <summary>
    /// The version that accepts any version at all.
    /// </summary>
    /// <remarks>
    /// Only on the version side. A wildcard id would approve everything, which
    /// is the same as having no list — and somebody writing it almost certainly
    /// meant to write a name.
    /// </remarks>
    public const string AnyVersion = "*";

    private ApprovedDependency(string text, string id, string version)
    {
        Text = text;
        Id = id;
        Version = version;
    }

    /// <summary>
    /// The entry as the policy wrote it, for a report that has to name it.
    /// </summary>
    public string Text { get; }

    /// <summary>The approved package or module name.</summary>
    public string Id { get; }

    /// <summary>The approved version, or <see cref="AnyVersion"/>.</summary>
    public string Version { get; }

    /// <summary>
    /// Reads one entry.
    /// </summary>
    /// <param name="entry">The entry as the policy wrote it.</param>
    /// <returns>
    /// The approval, or <see langword="null"/> when the entry carries no
    /// <c>@</c>, or either side of the last one is empty.
    /// </returns>
    public static ApprovedDependency? Parse(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var trimmed = entry.Trim();
        var at = trimmed.LastIndexOf('@');

        if (at <= 0)
        {
            // At zero as well as absent: an entry beginning with the separator
            // has no id, and a scoped name that reached here would have found a
            // later one.
            return null;
        }

        var id = trimmed[..at].Trim();
        var version = trimmed[(at + 1)..].Trim();

        return id.Length == 0 || version.Length == 0 || id.Contains('*', StringComparison.Ordinal)
            ? null
            : new ApprovedDependency(entry, id, version);
    }

    /// <summary>
    /// Whether this entry approves a declared dependency.
    /// </summary>
    /// <param name="dependency">The dependency the manifest declares.</param>
    /// <returns><see langword="true"/> when the id and the version both match.</returns>
    /// <remarks>
    /// The id is compared without regard to case, because package ecosystems
    /// treat it that way and a list that refused <c>serilog</c> for
    /// <c>Serilog</c> would be read as a defect. The version is compared as
    /// text, because <c>3.1.1</c> and <c>3.1.1.0</c> are different strings in
    /// every manifest and lockfile that will ever be compared with this list —
    /// treating them as equal would approve a version nobody wrote down.
    /// </remarks>
    public bool Matches(DependencyRequirement dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);

        return string.Equals(Id, dependency.Id, StringComparison.OrdinalIgnoreCase)
            && (Version == AnyVersion
                || string.Equals(Version, dependency.Version, StringComparison.OrdinalIgnoreCase));
    }
}
