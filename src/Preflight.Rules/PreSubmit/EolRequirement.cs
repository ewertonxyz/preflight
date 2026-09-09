namespace Preflight.Rules;

/// <summary>
/// What an attributes file says about the line ending of a path.
/// </summary>
/// <remarks>
/// Four answers rather than two, because "nothing was said" and "this path was
/// declared binary" are different facts with different consequences. Collapsing
/// them would judge a file the repository deliberately exempted by its line
/// endings, and leave the reader no way to fix it except by editing policy.
/// </remarks>
internal enum EolRequirement
{
    /// <summary>
    /// No entry matching the path said anything about line endings.
    /// </summary>
    /// <remarks>
    /// Also what a plain text declaration answers. Without an explicit ending
    /// beside it the client's answer depends on a machine-level setting and on
    /// the platform, so a rule that guessed one would give two verdicts for one
    /// commit on two machines.
    /// </remarks>
    Silent,

    /// <summary>A bare line feed is required.</summary>
    Lf,

    /// <summary>A carriage return and line feed are required.</summary>
    Crlf,

    /// <summary>
    /// The path is declared to have no text handling at all.
    /// </summary>
    /// <remarks>
    /// Stronger than silence: it suppresses the policy list as well, because a
    /// repository that declared a path binary has already answered the question
    /// the policy would otherwise ask.
    /// </remarks>
    Exempt,
}
