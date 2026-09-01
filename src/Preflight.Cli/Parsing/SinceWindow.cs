namespace Preflight.Cli.Parsing;

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
