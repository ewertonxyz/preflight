namespace Preflight.Rules;

/// <summary>
/// One line of what the version control client printed about a submodule.
/// </summary>
/// <param name="State">What the leading character said.</param>
/// <param name="Path">The submodule's path, relative to the workspace root.</param>
/// <remarks>
/// Its own type because reading the client's output format is a different
/// reason to change than deciding what to report about a submodule. The first
/// moves when the client's output does.
/// </remarks>
internal sealed record SubmoduleStatusLine(SubmoduleState State, string Path)
{
    /// <summary>
    /// Reads one line.
    /// </summary>
    /// <param name="line">The line, without its terminator.</param>
    /// <returns>
    /// What it says, or <see langword="null"/> when the line is blank.
    /// </returns>
    /// <remarks>
    /// The path is everything after the object name up to the last <c>" ("</c>,
    /// rather than a token taken by splitting on spaces. A content repository
    /// has directories with spaces in them, and a split would read the first
    /// word of one as the whole path — and then report a submodule at a path
    /// that does not exist. The suffix in parentheses is the client's
    /// description of the checked-out commit and is optional.
    /// </remarks>
    public static SubmoduleStatusLine? Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.Length == 0)
        {
            return null;
        }

        var state = line[0] switch
        {
            ' ' => SubmoduleState.UpToDate,
            '-' => SubmoduleState.Uninitialised,
            '+' => SubmoduleState.Diverged,
            'U' => SubmoduleState.Conflicted,
            _ => SubmoduleState.Unrecognised,
        };

        return new SubmoduleStatusLine(state, PathIn(line));
    }

    private static string PathIn(string line)
    {
        var afterObjectName = line.IndexOf(' ', 1);

        if (afterObjectName < 0)
        {
            return string.Empty;
        }

        var rest = line[(afterObjectName + 1)..];
        var describe = rest.LastIndexOf(" (", StringComparison.Ordinal);

        return (describe < 0 ? rest : rest[..describe]).Trim();
    }
}
