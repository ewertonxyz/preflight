namespace Preflight.Core.Tests.Policy;

using Preflight.Core.Policy;

/// <summary>
/// Fixes the pure edit-distance function used to power the "did you mean"
/// suggestions policy validation requires for an unknown rule id and
/// an unknown rule-object key.
/// </summary>
public sealed class LevenshteinDistanceTests
{
    [Fact]
    public void Compute_WithIdenticalStrings_ReturnsZero()
    {
        LevenshteinDistance.Compute("blocking", "blocking").ShouldBe(0);
    }

    [Theory]
    [InlineData("blockin", "blocking", 1)]
    [InlineData("sevirity", "severity", 1)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("core.presubmit.large-file", "core.presubmit.large-fyle", 1)]
    public void Compute_WithKnownPairs_ReturnsExpectedDistance(string a, string b, int expected)
    {
        LevenshteinDistance.Compute(a, b).ShouldBe(expected);
    }

    [Theory]
    [InlineData("blocking", "gating")]
    [InlineData("core.foo.bar", "core.baz.qux")]
    [InlineData("a", "abc")]
    public void Compute_IsSymmetric_ForArbitraryPairs(string a, string b)
    {
        LevenshteinDistance.Compute(a, b).ShouldBe(LevenshteinDistance.Compute(b, a));
    }

    [Theory]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "", 3)]
    [InlineData("", "", 0)]
    public void Compute_WithEmptyString_ReturnsLengthOfOther(string a, string b, int expected)
    {
        LevenshteinDistance.Compute(a, b).ShouldBe(expected);
    }
}
