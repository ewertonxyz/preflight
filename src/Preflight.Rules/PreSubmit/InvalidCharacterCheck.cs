namespace Preflight.Rules;

/// <summary>
/// Finds a character in a path that Windows refuses to write.
/// </summary>
/// <remarks>
/// A path containing one of these is created without complaint on Linux and
/// macOS and cannot be checked out on Windows at all. The clone fails, or
/// silently omits the file — which is the worse of the two, because the build
/// that follows fails somewhere else entirely.
/// </remarks>
internal sealed class InvalidCharacterCheck : IPathPortabilityCheck
{
    /// <summary>
    /// The name the policy selects this check by.
    /// </summary>
    public const string CheckName = "invalid-character";

    /// <summary>
    /// The characters Windows refuses inside a file or directory name.
    /// </summary>
    /// <remarks>
    /// Seven printable ones, plus the backslash — which is a separator on
    /// Windows and an ordinary character everywhere else, so a repository
    /// created on Linux can genuinely hold a file whose name contains one.
    /// The forward slash is absent because it is this tool's separator and
    /// arrives as structure rather than as content.
    /// </remarks>
    public static readonly char[] Refused = ['<', '>', ':', '"', '|', '?', '*', '\\'];

    /// <inheritdoc/>
    public string Name => CheckName;

    /// <inheritdoc/>
    public IReadOnlyList<PathDefect> Inspect(IReadOnlyList<string> paths, PathPortabilityLimits limits)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var defects = new List<PathDefect>();

        foreach (var path in paths)
        {
            // The first offending character, not every one of them. A name
            // written in the wrong encoding trips on most of its bytes, and one
            // finding per byte would bury the file it is about.
            var offender = path.FirstOrDefault(Refuses);

            if (offender != default)
            {
                defects.Add(new PathDefect(path, $"contains {Describe(offender)}", "no character Windows refuses"));
            }
        }

        return defects;
    }

    /// <remarks>
    /// Control characters are refused alongside the printable ones. They are
    /// invisible in every tool that would display the path, so the reader of a
    /// failed checkout has no way to see what is wrong without being told.
    /// </remarks>
    private static bool Refuses(char character) =>
        Array.IndexOf(Refused, character) >= 0 || char.IsControl(character);

    /// <remarks>
    /// A control character is named by its code point rather than printed,
    /// because printing it would put an invisible character in a message whose
    /// whole purpose is to say which character is there.
    /// </remarks>
    private static string Describe(char character) =>
        char.IsControl(character)
            ? $"the control character U+{(int)character:X4}"
            : $"'{character}'";
}
