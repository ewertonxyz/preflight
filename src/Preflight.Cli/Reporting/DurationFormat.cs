namespace Preflight.Cli.Reporting;

using System.Globalization;

/// <summary>
/// The single point every duration in every report is formatted through.
/// </summary>
/// <remarks>
/// <para>
/// Byte-identical output from identical input is required, and a second place
/// that formats a <see cref="TimeSpan"/> is the shortest path to two reports
/// that disagree about what <c>0.05</c> seconds is called. It was a private
/// method on the console reporter until the report needed the same numbers at a
/// different scale.
/// </para>
/// <para>
/// Every number is formatted with <see cref="CultureInfo.InvariantCulture"/>.
/// The author of this project works on a pt-BR machine, where the default
/// renders <c>0.4s</c> as <c>0,4s</c>, and CI is almost certainly en-US: with
/// the ambient culture, that guarantee would hold on each machine separately
/// and fail between them.
/// </para>
/// </remarks>
public static class DurationFormat
{
    /// <summary>
    /// One decimal of seconds, as the console prints a rule's duration.
    /// </summary>
    public static string Seconds(TimeSpan duration) =>
        duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";

    /// <summary>
    /// The scale the report prints, which has to hold both a rule at
    /// <c>14.9s</c> and a build at <c>38m02s</c> in the same column.
    /// </summary>
    /// <remarks>
    /// The minute and hour forms zero-pad their smaller unit — <c>38m02s</c>,
    /// not <c>38m2s</c> — so the column stays aligned and two reports stay
    /// diffable. Seconds are dropped above an hour: a build measured to the
    /// second and reported to the second suggests a precision that a p50 over
    /// twenty-seven samples does not have.
    /// </remarks>
    public static string Scaled(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
        {
            return Seconds(duration);
        }

        var total = (long)duration.TotalSeconds;
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var seconds = total % 60;

        return hours == 0
            ? Number(minutes) + "m" + Padded(seconds) + "s"
            : Number(hours) + "h" + Padded(minutes) + "m";
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Padded(long value) => value.ToString("00", CultureInfo.InvariantCulture);
}
