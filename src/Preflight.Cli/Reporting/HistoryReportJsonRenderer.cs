namespace Preflight.Cli.Reporting;

using System.Text.Json;
using Preflight.Core.History;

/// <summary>
/// Renders a history report as JSON, for <c>report --format json</c>.
/// </summary>
/// <remarks>
/// A second renderer over <see cref="HistoryReport"/>, not a scrape of the
/// console screen. The report was already computed as data and rendered
/// as text in a separate type, which is what makes this cheap. What it may and
/// may not say is decided once, and <see cref="HistoryReportDocument"/> is
/// where that decision lives.
/// </remarks>
public sealed class HistoryReportJsonRenderer
{
    private readonly TextWriter _output;

    public HistoryReportJsonRenderer(TextWriter output)
    {
        _output = output;
    }

    /// <summary>
    /// Writes the whole document.
    /// </summary>
    /// <remarks>
    /// The window travels as a <see cref="TimeSpan"/> and not as the text the
    /// user typed. <c>720h</c> and <c>30d</c> are the same window, and the
    /// distinction between them is worth keeping on the screen — where the
    /// header repeats what somebody asked for — and worth nothing to a consumer
    /// that is going to compare it against a number.
    /// </remarks>
    public void Report(HistoryReport report, SinceWindow window)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(window);

        _output.Write(JsonSerializer.Serialize(
            HistoryReportDocument.For(report, window.Value),
            RunEventDocument.Indented));

        _output.Write('\n');
    }
}
