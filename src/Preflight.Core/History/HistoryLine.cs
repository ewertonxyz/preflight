namespace Preflight.Core.History;

using System.Text;
using System.Text.Json;
using Preflight.Core.Execution;

/// <summary>
/// Turns one event into the single line the NDJSON history holds.
/// </summary>
/// <remarks>
/// <para>
/// A line is capped at 64 KB, and the cap is counted in <b>bytes</b> of the
/// serialised record — what will actually be written — rather than in
/// characters of anything. Today the two coincide, because the default
/// serialiser escapes everything outside ASCII; they stop coinciding the moment
/// somebody relaxes the encoder, and a limit that quietly became a character
/// limit is how a line ends up longer than the size a single append can be
/// relied on to write without another process interleaving into the middle of
/// it.
/// </para>
/// <para>
/// Measuring the serialised form has a second consequence worth stating: an
/// accented message costs six bytes a character once escaped, so a rule
/// flooding findings in any language other than English reaches the cap six
/// times sooner than its character count suggests. That falls out of measuring
/// the right thing rather than needing a rule of its own.
/// </para>
/// <para>
/// The record is also one line as a matter of mechanism, not of hope. Every
/// string in it goes through <see cref="JsonSerializer"/>, whose default
/// encoder escapes
/// control characters and everything outside ASCII — so a finding message
/// containing a newline, a carriage return or U+2028 cannot split the record in
/// two. That is the second way an append-only format loses data, and unlike the
/// first it happens on a local disk with a single process.
/// </para>
/// </remarks>
public static class HistoryLine
{
    /// <summary>The per-line limit, in bytes.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>
    /// The line for a run, replaced by the truncated summary when the full
    /// record would not fit.
    /// </summary>
    public static string ForRun(RunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var full = JsonSerializer.Serialize(RunEventDocument.For(result), RunEventDocument.SingleLine);

        return Encoding.UTF8.GetByteCount(full) <= MaxBytes
            ? full
            : JsonSerializer.Serialize(RunEventDocument.Truncated(result), RunEventDocument.SingleLine);
    }

    /// <summary>
    /// The line for a measured child process.
    /// </summary>
    /// <remarks>
    /// No cap, because there is nothing here that grows: a label, two instants
    /// and a command line. The 64 KB limit exists for a finding list.
    /// </remarks>
    public static string ForExternal(ExternalMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);

        return JsonSerializer.Serialize(ExternalEventDocument.For(measurement), RunEventDocument.SingleLine);
    }
}
