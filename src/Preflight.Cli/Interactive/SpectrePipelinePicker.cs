namespace Preflight.Cli.Interactive;

using Spectre.Console;

/// <summary>
/// The picker that actually draws.
/// </summary>
/// <remarks>
/// <para>
/// This project's entire use of Spectre.Console, and it lives here rather than
/// anywhere near <c>Reporting/</c>. Two systems deciding the same ANSI bytes is
/// how a golden file stops being the truth — the snapshot suite holds the
/// report's exact output and cannot arbitrate between two writers — and an
/// architecture test holds that boundary.
/// </para>
/// <para>
/// Nothing in here is unit tested, and that is the decision rather than the
/// gap: a test over these three lines would be a test of the library. What is
/// tested is <see cref="SelectionModel"/>, which is everything this type is
/// given, and <see cref="PipelinePicker.Choose"/>, which is everything that
/// decides whether it is reached at all.
/// </para>
/// </remarks>
public sealed class SpectrePipelinePicker : IPipelinePicker
{
    private readonly IAnsiConsole _console;

    /// <summary>Draws on the real terminal.</summary>
    public SpectrePipelinePicker()
        : this(AnsiConsole.Console)
    {
    }

    /// <param name="console">Where to draw.</param>
    public SpectrePipelinePicker(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);

        _console = console;
    }

    /// <inheritdoc />
    public string Pick(SelectionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        var labels = new List<string>(model.Choices.Count);

        for (var index = 0; index < model.Choices.Count; index++)
        {
            var choice = model.Choices[index];

            // The active row is marked rather than moved to the top. Moving it
            // would make the list order depend on machine state, and a menu
            // whose rows are somewhere else on a colleague's screen is a menu
            // two people cannot talk about.
            var label = Markup.Escape(
                index == model.ActiveIndex && choice.IsActive ? $"* {choice.Label}" : choice.Label);

            rows[label] = choice.Value;
            labels.Add(label);
        }

        var chosen = _console.Prompt(
            new SelectionPrompt<string>()
                .Title(Markup.Escape(model.Prompt))
                .AddChoices(labels));

        return rows[chosen];
    }
}
