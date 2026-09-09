namespace Preflight.Rules;

/// <summary>
/// The attributes file, read for the two questions rules ask it: which paths
/// were declared to live in LFS, and which line ending a path was declared to
/// have.
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
/// <para>
/// Last-rule-wins applies <em>per attribute</em>, so each question walks back to
/// the last entry that mentions its own attribute and ignores entries that
/// mention only the other. One shared walk was the obvious shape and is a silent
/// regression the moment a second attribute exists: an LFS line followed by an
/// ordinary line-ending line for the same pattern would answer "not tracked",
/// and the rule that keeps a real binary out of the history would go quiet —
/// with no branch changed anywhere, so nothing about the coverage would move
/// either.
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
    public bool DeclaresAnyLfsPattern => _entries.Any(entry => entry.Lfs == true);

    /// <summary>
    /// Whether the file declared a line ending for any path at all.
    /// </summary>
    /// <remarks>
    /// The counterpart of the question above, and asked for the same reason: an
    /// attributes file that says nothing about line endings has to be
    /// distinguishable from one that does and happened to match nothing.
    /// </remarks>
    public bool DeclaresAnyEol => _entries.Any(entry => entry.Eol is not null);

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

            var attributes = fields.AsSpan(1);
            var lfs = LfsState(attributes);
            var eol = EolState(attributes);

            // A line that answers neither question is not an entry at all.
            // Recording it would give it a state nobody wrote, and
            // last-rule-wins would then let it override the line above it.
            if (lfs is not null || eol is not null)
            {
                entries.Add(new Entry(GlobPattern.Compile(Translate(fields[0])), lfs, eol));
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
    /// <see langword="true"/> when the last entry mentioning the LFS filter and
    /// matching the path turns it on.
    /// </returns>
    public bool SendsToLfs(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        return LastMatching(relativePath, entry => entry.Lfs) ?? false;
    }

    /// <summary>
    /// Which line ending this path was declared to have.
    /// </summary>
    /// <param name="relativePath">
    /// A path relative to the workspace root, written with forward slashes.
    /// </param>
    /// <returns>
    /// What the last entry mentioning line endings and matching the path said.
    /// </returns>
    public EolRequirement EolFor(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        return LastMatching(relativePath, entry => entry.Eol) ?? EolRequirement.Silent;
    }

    /// <summary>
    /// What the last entry matching the path and mentioning this attribute said
    /// about it.
    /// </summary>
    /// <remarks>
    /// The walk runs backwards and stops at the first hit rather than folding
    /// every match together, and it steps over entries that say nothing about
    /// the attribute being asked for — which is what stops one question
    /// answering the other's.
    /// </remarks>
    private T? LastMatching<T>(string relativePath, Func<Entry, T?> state)
        where T : struct
    {
        for (var index = _entries.Count - 1; index >= 0; index--)
        {
            if (state(_entries[index]) is { } value && _entries[index].Pattern.Matches(relativePath))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a line says anything about the LFS filter, and what it says.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> to turn it on, <see langword="false"/> to turn it
    /// off, and <see langword="null"/> when the line does not mention it.
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
    /// Whether a line says anything about line endings, and what it says.
    /// </summary>
    /// <returns>
    /// The requirement it declares, or <see langword="null"/> when the line does
    /// not mention the question at all.
    /// </returns>
    /// <remarks>
    /// A plain <c>text</c> or <c>text=auto</c> with no ending beside it answers
    /// "silent" rather than nothing: the line does speak about text handling, so
    /// it ends the walk, but what the client would actually write depends on a
    /// machine-level setting and on the platform. Guessing there would make one
    /// commit produce two verdicts on two machines.
    /// </remarks>
    private static EolRequirement? EolState(ReadOnlySpan<string> attributes)
    {
        EolRequirement? state = null;

        foreach (var attribute in attributes)
        {
            state = attribute switch
            {
                "eol=lf" => EolRequirement.Lf,
                "eol=crlf" => EolRequirement.Crlf,
                "-text" or "!text" or "binary" => EolRequirement.Exempt,
                "text" or "text=auto" => EolRequirement.Silent,
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
    /// One line of an attributes file: a pattern, and what it says about each
    /// question a rule asks. A null means the line was silent on that one.
    /// </summary>
    private sealed record Entry(GlobPattern Pattern, bool? Lfs, EolRequirement? Eol);
}
