namespace Preflight.Core.History;

/// <summary>
/// A percentile pair over one series, together with the sample it came from.
/// </summary>
/// <remarks>
/// The sample size travels with the numbers rather than being recomputed by
/// whoever renders them. The report prints <c>(n=142)</c> next to every
/// percentile precisely so the number cannot be quoted without it.
/// </remarks>
/// <param name="SampleSize">How many observations there were.</param>
/// <param name="P50">The median, or <see langword="null"/> below five observations.</param>
/// <param name="P95">The 95th, or <see langword="null"/> below fifty.</param>
public sealed record DurationSummary(int SampleSize, TimeSpan? P50, TimeSpan? P95)
{
    /// <summary>An empty series.</summary>
    public static DurationSummary Empty { get; } = new(0, null, null);

    /// <summary>
    /// The summary of <paramref name="sample"/>, with the sample-size minimums
    /// applied.
    /// </summary>
    public static DurationSummary Of(IReadOnlyList<TimeSpan> sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        return new DurationSummary(sample.Count, PercentileCalculator.P50(sample), PercentileCalculator.P95(sample));
    }
}
