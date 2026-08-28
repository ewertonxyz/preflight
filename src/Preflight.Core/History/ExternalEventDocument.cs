namespace Preflight.Core.History;

/// <summary>
/// The JSON shape of an <c>external</c> event in the history.
/// </summary>
public static class ExternalEventDocument
{
    /// <summary>The <c>type</c> discriminator a measurement is written under.</summary>
    public const string EventType = "external";

    /// <summary>
    /// The record for one measured child process.
    /// </summary>
    public static object For(ExternalMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);

        return new
        {
            type = EventType,
            label = measurement.Label,
            startedAt = measurement.StartedAt,
            durationMs = (long)measurement.Duration.TotalMilliseconds,
            exitCode = measurement.ExitCode,
            command = measurement.Command,
        };
    }
}
