namespace Preflight.Rules;

/// <summary>
/// The attributes file, read for the one question a rule asks it: which paths
/// were declared to live in LFS.
/// </summary>
/// <remarks>
/// <para>
/// Its own type because knowing what an attributes file says is a different
/// reason to change than knowing what to report about a file that disobeys it.
/// The first moves when the attributes format does.
/// </para>
/// <para>
/// The entries stay in file order and the decision is taken per path, because
/// the format is last-rule-wins: a broad line sending every <c>.psd</c> to LFS
/// followed by a narrow one taking a vendored directory back out means the
/// vendored files are not in LFS. Collapsing the file into a set of "LFS
/// patterns" would lose the second line entirely and report a violation on a
/// file the repository deliberately exempted.
/// </para>
/// </remarks>
internal sealed class GitAttributes
{
    private readonly IReadOnlyList<Entry> _entries;

    private GitAttributes(IReadOnlyList<Entry> entries)
    {
        _entries = entries;
    }

    /// <summary>
    /// Whether the file declared any path as living in LFS at all.
    /// </summary>
    /// <remarks>
    /// Asked before any path is examined, so that an attributes file with no
    /// LFS line at all reports that nothing was checked rather than that
    /// everything passed.
    /// </remarks>
    public bool DeclaresAnyLfsPattern => _entries.Any(entry => entry.Lfs);

    /// <summary>
    /// Reads an attributes file.
    /// </summary>
    /// <param name="content">The whole file, as text.</param>
    /// <returns>The entries it declares, in the order it declares them.</returns>
    /// <remarks>
    /// Comments, blank lines and macro definitions are skipped. A macro line
    /// begins <c>[attr]</c> and defines a name for a set of attributes rather
    /// than applying them to a path, so treating it as a pattern would match a
    /// file literally called <c>[attr]binary</c> and nothing else.
    /// </remarks>
    public static GitAttributes Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var entries = new List<Entry>();

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed.StartsWith("[attr]", StringComparison.Ordinal))
            {
                continue;
            }

            var fields = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (fields.Length < 2)
            {
                continue;
            }

            if (LfsState(fields.AsSpan(1)) is { } lfs)
            {
                entries.Add(new Entry(GlobPattern.Compile(Translate(fields[0])), lfs));
            }
        }

        return new GitAttributes(entries);
    }

    /// <summary>
    /// Whether this path was declared to live in LFS.
    /// </summary>
    /// <param name="relativePath">
    /// A path relative to the workspace root, written with forward slashes.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the last entry matching the path turns the
    /// LFS filter on.
    /// </returns>
    public bool SendsToLfs(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        // The last matching line wins, so the walk runs backwards and stops at
        // the first hit rather than folding every match together.
        for (var index = _entries.Count - 1; index >= 0; index--)
        {
            if (_entries[index].Pattern.Matches(relativePath))
            {
                return _entries[index].Lfs;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a line says anything about the LFS filter, and what it says.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> to turn it on, <see langword="false"/> to turn it
    /// off, and <see langword="null"/> when the line does not mention it — in
    /// which case the line is not an entry this type carries at all.
    /// </returns>
    /// <remarks>
    /// Compared token by token, and the value is compared exactly. A substring
    /// test would accept <c>filter=lfsx</c>, which is somebody else's filter,
    /// and would miss <c>-filter</c> and <c>!filter</c>, which are the two ways
    /// the format has of taking a file back out.
    /// </remarks>
    private static bool? LfsState(ReadOnlySpan<string> attributes)
    {
        bool? state = null;

        foreach (var attribute in attributes)
        {
            state = attribute switch
            {
                "filter=lfs" => true,
                "-filter" or "!filter" => false,
                _ => state,
            };
        }

        return state;
    }

    /// <summary>
    /// Rewrites an attributes pattern into the dialect
    /// <see cref="GlobPattern"/> speaks.
    /// </summary>
    /// <remarks>
    /// The two agree everywhere except on a pattern with no separator in it,
    /// which the attributes format applies at every depth and the policy format
    /// applies only at the root. That is the common case — an extension applied
    /// to the whole tree — so leaving it untranslated would silently stop
    /// matching almost everything. A leading slash means the opposite: anchored
    /// at the root, which is what the policy dialect already does once the
    /// slash is removed.
    /// </remarks>
    private static string Translate(string pattern) => pattern switch
    {
        ['/', .. var anchored] => anchored,
        _ when !pattern.Contains('/', StringComparison.Ordinal) => "**/" + pattern,
        _ => pattern,
    };

    /// <summary>
    /// One line of an attributes file: a pattern, and whether it turns the LFS
    /// filter on or off.
    /// </summary>
    private sealed record Entry(GlobPattern Pattern, bool Lfs);
}
