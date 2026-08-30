namespace Preflight.Rules;

/// <summary>
/// The text handling every rule shares when it puts something a tool printed
/// into a finding.
/// </summary>
internal static class FindingText
{
    /// <summary>
    /// Trims whitespace and caps the result at <paramref name="limit"/>
    /// characters, marking the cut with an ellipsis.
    /// </summary>
    /// <remarks>
    /// A cap has to exist because the text is whatever a child process chose to
    /// print, and a compiler given a bad argument prints its entire help. That
    /// text is rendered in the console report and stored in the run's history,
    /// where one record is capped at 64 KB — so an uncapped copy either fills
    /// the terminal or costs the record the fields that come after it.
    ///
    /// The limit is the caller's because the two callers are not comparable: a
    /// version banner that did not parse is one line and worth showing whole,
    /// while a failed compile is an arbitrary amount of build log and only the
    /// opening of it says anything.
    /// </remarks>
    public static string Truncate(string text, int limit)
    {
        var trimmed = text.Trim();

        return trimmed.Length <= limit ? trimmed : trimmed[..limit] + "…";
    }
}
