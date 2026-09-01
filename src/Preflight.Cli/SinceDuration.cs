namespace Preflight.Cli;

using System.Globalization;

/// <summary>
/// The window <c>preflight report --since</c> was asked for.
/// </summary>
/// <param name="Amount">How many units.</param>
/// <param name="Unit">Which unit, as the user typed it.</param>
/// <param name="Value">The window itself.</param>
public sealed record SinceWindow(long Amount, char Unit, TimeSpan Value)
{
    /// <summary>
    /// The window in words, for the report's own header.
    /// </summary>
    /// <remarks>
    /// Rendered from what the user typed rather than from
    /// <see cref="Value"/>, because <c>720h</c> and <c>30d</c> are the same
    /// window and only one of them is what somebody asked for.
    /// </remarks>
    public string Describe()
    {
        var noun = Unit switch
        {
            'd' => "day",
            'h' => "hour",
            _ => "minute",
        };

        var plural = Amount == 1 ? string.Empty : "s";

        return $"{Amount.ToString(CultureInfo.InvariantCulture)} {noun}{plural}";
    }
}

/// <summary>
/// Parses the <c>--since</c> grammar: an integer and one of three unit letters.
/// </summary>
/// <remarks>
/// Deliberately the smallest grammar that answers the report's question. Weeks
/// and months, or ISO-8601, are each a parser to write and test before a single
/// date is compared — the same argument the workspace manifest makes for
/// expressing a version range as two explicit bounds instead of a range syntax.
/// </remarks>
public static class SinceDuration
{
    /// <summary>The unit letters, in the order the error message names them.</summary>
    public static IReadOnlyList<string> AcceptedUnits { get; } = ["d", "h", "m"];

    /// <summary>
    /// The window <paramref name="value"/> names, or <see langword="null"/>
    /// when it names none.
    /// </summary>
    /// <remarks>
    /// Zero and negative windows are refused rather than clamped.
    /// <c>--since 0d</c> would report over an empty window and print zeros,
    /// which reads exactly like a month in which nothing was ever validated.
    /// </remarks>
    public static SinceWindow? Parse(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 2)
        {
            return null;
        }

        var unit = value[^1];

        var ticksPerUnit = unit switch
        {
            'd' => TimeSpan.TicksPerDay,
            'h' => TimeSpan.TicksPerHour,
            'm' => TimeSpan.TicksPerMinute,
            _ => 0L,
        };

        if (ticksPerUnit == 0)
        {
            return null;
        }

        // NumberStyles.None, so a sign, a decimal point, a thousands separator
        // and surrounding whitespace are all refused here rather than silently
        // reinterpreted. '-1d' is a question, not a window.
        if (!long.TryParse(value[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0)
        {
            return null;
        }

        // Checked before multiplying rather than caught after. TimeSpan throws
        // on overflow, and an exception escaping a parser turns a clean exit 2
        // into an unhandled crash.
        return amount > TimeSpan.MaxValue.Ticks / ticksPerUnit
            ? null
            : new SinceWindow(amount, unit, TimeSpan.FromTicks(amount * ticksPerUnit));
    }
}
