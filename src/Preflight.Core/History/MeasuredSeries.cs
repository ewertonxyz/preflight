namespace Preflight.Core.History;

/// <summary>One measured label and how long it took.</summary>
/// <param name="Label">The <c>--label</c> the measurement was filed under.</param>
/// <param name="Duration">Its percentiles.</param>
public sealed record MeasuredSeries(string Label, DurationSummary Duration);
