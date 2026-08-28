namespace Preflight.Core.History;

/// <summary>
/// Nearest rank, no interpolation, and a minimum sample below which there is no
/// answer.
/// </summary>
/// <remarks>
/// <para>
/// Both halves are the design. Interpolating between two observations invents a
/// value nothing measured; and with <c>n = 27</c> a p95 is the 26th of 27
/// ordered values — practically the maximum, dressed as a percentile. Section
/// 10.3 calls presenting that the kind of number somebody uses in a meeting and
/// cannot defend afterwards.
/// </para>
/// <para>
/// The minimum is a parameter rather than a table keyed by percentile, because
/// the two callers are the only two percentiles the report has and each one
/// states its own requirement at the call site, where a reader can see it.
/// </para>
/// </remarks>
public static class PercentileCalculator
{
    /// <summary>The p50 is reported from five observations.</summary>
    public const int MinimumSampleForP50 = 5;

    /// <summary>The p95 needs fifty.</summary>
    public const int MinimumSampleForP95 = 50;

    /// <summary>The 50th percentile, or <see langword="null"/> below the minimum sample.</summary>
    public static TimeSpan? P50(IReadOnlyList<TimeSpan> sample) =>
        Compute(sample, 50, MinimumSampleForP50);

    /// <summary>The 95th percentile, or <see langword="null"/> below the minimum sample.</summary>
    public static TimeSpan? P95(IReadOnlyList<TimeSpan> sample) =>
        Compute(sample, 95, MinimumSampleForP95);

    /// <summary>
    /// The value at <paramref name="percentile"/> by nearest rank.
    /// </summary>
    /// <param name="sample">The observations, in any order.</param>
    /// <param name="percentile">Between 1 and 100.</param>
    /// <param name="minimumSample">
    /// Below this many observations there is no answer, and the caller prints
    /// what is missing instead of a number.
    /// </param>
    public static TimeSpan? Compute(IReadOnlyList<TimeSpan> sample, int percentile, int minimumSample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        if (sample.Count < minimumSample)
        {
            return null;
        }

        var ordered = sample.Order().ToArray();

        // Nearest rank: the smallest value at or above which the requested share
        // of the sample sits. Ceiling, so p50 of five values is the third and
        // not the average of the second and third.
        var rank = (int)Math.Ceiling(percentile / 100.0 * ordered.Length);

        return ordered[rank - 1];
    }
}
