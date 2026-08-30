namespace Preflight.Rules;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// One path pattern from a policy, compiled once and matched many times.
/// </summary>
/// <remarks>
/// <para>
/// Its own type rather than a method on <see cref="ForbiddenPathsRule"/>,
/// because translating a glob dialect into a regular expression is a second
/// reason for that file to change. The rule decides which files are forbidden
/// and what the report says about them; this decides what <c>**</c> means. The
/// two move for different reasons and now sit apart.
/// </para>
/// <para>
/// It keeps the text it was compiled from because the finding names the pattern
/// that matched, not the expression it became. A reader asked to fix a
/// violation needs the line they wrote in their policy, and
/// <c>^(?:.*/)?[^/]*\.pfx$</c> is not it.
/// </para>
/// </remarks>
internal sealed class GlobPattern
{
    private readonly Regex _expression;

    private GlobPattern(string text, Regex expression)
    {
        Text = text;
        _expression = expression;
    }

    /// <summary>
    /// The pattern as the policy wrote it.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Translates one glob into a regular expression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>**</c> crosses directory separators and <c>*</c> does not, which is
    /// the distinction the whole pattern language turns on: <c>*.pfx</c> means
    /// a certificate at the root, <c>**/*.pfx</c> means one anywhere.
    /// Collapsing them would make every pattern accidentally recursive.
    /// </para>
    /// <para>
    /// Matching is case-insensitive. Windows and macOS filesystems are, so a
    /// case-sensitive matcher would let <c>Secrets/KEY.PFX</c> through on the
    /// machines most developers use — and letting a secret through is the
    /// direction of error that matters here.
    /// </para>
    /// <para>
    /// Built at every run rather than cached, and not with
    /// <see cref="RegexOptions.Compiled"/>. The patterns come from policy, so
    /// there is no fixed set to generate ahead of time, and emitting IL for a
    /// handful of short expressions costs more than interpreting them for the
    /// few thousand paths one run matches against.
    /// </para>
    /// </remarks>
    public static GlobPattern Compile(string pattern)
    {
        var expression = new StringBuilder("^");

        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];

            if (character == '*')
            {
                if (index + 1 < pattern.Length && pattern[index + 1] == '*')
                {
                    // '**/' also matches zero directories, so '**/*.pfx' catches
                    // a certificate at the root as well as a nested one. Without
                    // that, every pattern would need writing twice.
                    index++;

                    if (index + 1 < pattern.Length && pattern[index + 1] == '/')
                    {
                        index++;
                        expression.Append("(?:.*/)?");

                        continue;
                    }

                    expression.Append(".*");

                    continue;
                }

                expression.Append("[^/]*");

                continue;
            }

            expression.Append(character == '?' ? "[^/]" : Regex.Escape(character.ToString()));
        }

        expression.Append('$');

        return new GlobPattern(
            pattern,
            new Regex(expression.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    /// <summary>
    /// Whether a path, written with forward slashes and relative to the
    /// workspace root, matches.
    /// </summary>
    public bool Matches(string relativePath) => _expression.IsMatch(relativePath);
}
