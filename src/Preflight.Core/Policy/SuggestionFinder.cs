namespace Preflight.Core.Policy;

/// <summary>
/// Picks the closest candidate strings to an unrecognised input, for the "did
/// you mean" suggestions a rejected key or rule id comes back with.
/// </summary>
/// <remarks>
/// Suggesting by edit distance leaves the cutoff and the tie-break rule open.
/// Chosen here: a candidate is offered only when its distance is at most 3, or
/// at most half the input's length, whichever is larger — a farther candidate
/// is noise, not help. On a tie, every candidate at the minimum distance is
/// returned, alphabetically, rather than picking one arbitrarily; the three
/// call sites (unknown rule id, unknown rule-object key, and eventually a
/// missing <c>DependsOn</c> target in the graph) all share this one
/// implementation so their tie-break behaviour cannot drift apart silently.
/// </remarks>
public static class SuggestionFinder
{
    public static IReadOnlyList<string> FindClosest(string input, IEnumerable<string> candidates)
    {
        var threshold = Math.Max(3, (int)Math.Ceiling(input.Length / 2.0));

        var scored = candidates
            .Select(candidate => (Candidate: candidate, Distance: LevenshteinDistance.Compute(input, candidate)))
            .Where(scored => scored.Distance <= threshold)
            .ToArray();

        if (scored.Length == 0)
        {
            return [];
        }

        var minimumDistance = scored.Min(scored => scored.Distance);

        return [.. scored
            .Where(scored => scored.Distance == minimumDistance)
            .Select(scored => scored.Candidate)
            .OrderBy(candidate => candidate, StringComparer.Ordinal)];
    }
}
