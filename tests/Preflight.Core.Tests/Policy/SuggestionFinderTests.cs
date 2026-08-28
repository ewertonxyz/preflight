namespace Preflight.Core.Tests.Policy;

using Preflight.Core.Policy;

/// <summary>
/// Fixes the threshold and tie-break rule for "did you mean" suggestions.
/// </summary>
/// <remarks>
/// policy validation requires a suggestion by edit distance but names
/// no cutoff or tie-break rule. The cutoff chosen here: suggest only when the
/// minimum distance is at most 3, or at most half the input's length,
/// whichever is larger — a candidate further than that is noise, not help. A
/// tie is not resolved by picking one arbitrarily: every candidate at the
/// minimum distance is returned, alphabetically.
/// </remarks>
public sealed class SuggestionFinderTests
{
    [Fact]
    public void FindClosest_WithOneCandidateWithinThreshold_ReturnsThatCandidate()
    {
        var result = SuggestionFinder.FindClosest("blockin", ["enabled", "blocking", "gating"]);

        result.ShouldBe(["blocking"]);
    }

    [Fact]
    public void FindClosest_WithNoCandidateWithinThreshold_ReturnsEmpty()
    {
        var result = SuggestionFinder.FindClosest("xyz", ["enabled", "blocking"]);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void FindClosest_WithTiedDistance_ReturnsAllTiedCandidatesAlphabetically()
    {
        // "ca" is exactly one insertion away from both "cat" and "car".
        var result = SuggestionFinder.FindClosest("ca", ["cat", "car"]);

        result.ShouldBe(["car", "cat"]);
    }

    [Theory]
    [InlineData("abd", new[] { "abc" }, true)]
    [InlineData("core.presubmit.large-fila", new[] { "core.presubmit.large-file" }, true)]
    public void FindClosest_ThresholdIsMaxOfThreeOrHalfInputLength(string input, string[] candidates, bool expectMatch)
    {
        var result = SuggestionFinder.FindClosest(input, candidates);

        if (expectMatch)
        {
            result.ShouldNotBeEmpty();
        }
        else
        {
            result.ShouldBeEmpty();
        }
    }
}
