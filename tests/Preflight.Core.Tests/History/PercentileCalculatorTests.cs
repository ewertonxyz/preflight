namespace Preflight.Core.Tests.History;

using Preflight.Core.History;

/// <summary>
/// The two honesty decisions of the report, asserted
/// against the numbers the section itself names.
/// </summary>
public sealed class PercentileCalculatorTests
{
    /// <summary>
    /// Nearest rank, never interpolation.
    /// </summary>
    /// <remarks>
    /// The <c>n=27, p95</c> row is the one the report works through in prose:
    /// the answer is the 26th of 27 ordered values. An implementation that
    /// interpolated would return something between the 25th and the 26th, which
    /// is a number nothing measured.
    /// </remarks>
    [Theory]
    [InlineData(5, 50, 3)]
    [InlineData(6, 50, 3)]
    [InlineData(27, 50, 14)]
    [InlineData(27, 95, 26)]
    [InlineData(50, 95, 48)]
    [InlineData(51, 95, 49)]
    [InlineData(100, 50, 50)]
    [InlineData(100, 95, 95)]
    [InlineData(142, 50, 71)]
    public void Compute_ByNearestRank_PicksTheRankedValueWithoutInterpolating(
        int size,
        int percentile,
        int expectedSeconds)
    {
        var sample = Seconds(size);

        PercentileCalculator.Compute(sample, percentile, minimumSample: 1)
            .ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    /// <remarks>
    /// The sample arrives in whatever order the history was read in, and a
    /// percentile over unsorted input is a value at an arbitrary position.
    /// </remarks>
    [Fact]
    public void Compute_ForAnUnorderedSample_SortsItFirst()
    {
        PercentileCalculator.Compute(
                [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)],
                50,
                minimumSample: 1)
            .ShouldBe(TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// Below the minimum sample there is no answer, and the boundary is exact.
    /// </summary>
    /// <remarks>
    /// Both edges are in the same table on purpose. An off-by-one here does not
    /// throw: it publishes a p95 over 49 observations, which is the maximum
    /// dressed as a percentile — exactly what the report refuses.
    /// </remarks>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public void P50_BelowFiveObservations_HasNoAnswer(int size, bool expected) =>
        (PercentileCalculator.P50(Seconds(size)) is not null).ShouldBe(expected);

    [Theory]
    [InlineData(0, false)]
    [InlineData(27, false)]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(51, true)]
    public void P95_BelowFiftyObservations_HasNoAnswer(int size, bool expected) =>
        (PercentileCalculator.P95(Seconds(size)) is not null).ShouldBe(expected);

    /// <remarks>
    /// The pair of minimums is deliberately asymmetric, and the constants are the
    /// only place that says so. A p50 is reported from five observations while a
    /// p95 waits for fifty.
    /// </remarks>
    [Fact]
    public void Minimums_AreTheOnesSection103States()
    {
        PercentileCalculator.MinimumSampleForP50.ShouldBe(5);
        PercentileCalculator.MinimumSampleForP95.ShouldBe(50);
    }

    private static IReadOnlyList<TimeSpan> Seconds(int count) =>
        [.. Enumerable.Range(1, count).Select(value => TimeSpan.FromSeconds(value))];
}
