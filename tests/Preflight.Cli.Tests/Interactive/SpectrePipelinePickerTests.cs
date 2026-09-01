namespace Preflight.Cli.Tests.Interactive;

using Preflight.Cli.Interactive;
using Spectre.Console;
using Spectre.Console.Testing;

/// <summary>
/// Drives the real picker against a console it can draw on and a keyboard that
/// answers it.
/// </summary>
/// <remarks>
/// <para>
/// The selection model is tested and the rendering is not, and that line is the
/// right one: asserting on the escape sequences Spectre.Console emits would be
/// asserting about Spectre.Console.
/// What sat on the wrong side of it was everything this type does with
/// the model before handing it over — building the label list, marking the row
/// the machine is on, and mapping the chosen label back to the value the caller
/// asked for. None of that is the library's code, and none of it was reached by
/// anything.
/// </para>
/// <para>
/// So the assertions here are about the answer and the rows, never about the
/// bytes. A <see cref="TestConsole"/> in interactive mode with keys pushed into
/// it is the smallest arrangement that runs the real prompt, and the reverse
/// mapping is the part that fails hardest: it is a dictionary lookup keyed by a
/// string this type built itself, so a change to how a label is composed that
/// forgot the lookup would throw <see cref="KeyNotFoundException"/> in front of
/// somebody choosing a version.
/// </para>
/// </remarks>
public sealed class SpectrePipelinePickerTests
{
    private static SelectionModel Model(params SelectionChoice[] choices) =>
        new(choices, ActiveIndex: 0, "Which version of 'projecta' should this machine use?");

    private static SelectionChoice Choice(string label, string value, bool active = false) =>
        new(label, value, active, IsAllowed: true);

    /// <summary>A console that behaves like a terminal, with keys queued.</summary>
    /// <remarks>
    /// <c>Interactive()</c> is required: without it the prompt refuses, because
    /// Spectre.Console makes the same judgement about a non-interactive console
    /// that <c>PipelinePicker.Choose</c> makes one layer up. The two refusals
    /// agreeing is a coincidence worth not relying on, which is why the gate is
    /// in front of this type rather than inside it.
    /// </remarks>
    private static TestConsole Console(params ConsoleKey[] keys)
    {
        var console = new TestConsole().Interactive();

        foreach (var key in keys)
        {
            console.Input.PushKey(key);
        }

        return console;
    }

    [Fact]
    public void Pick_WhenTheFirstRowIsAccepted_ReturnsItsValue()
    {
        var console = Console(ConsoleKey.Enter);

        new SpectrePipelinePicker(console)
            .Pick(Model(Choice("1.10.0", "projecta@1.10.0"), Choice("1.4.0", "projecta@1.4.0")))
            .ShouldBe("projecta@1.10.0");
    }

    /// <remarks>
    /// The reverse mapping, which is the whole reason this type holds a
    /// dictionary. What the person sees is a label; what the caller needs is the
    /// selector. Returning the label would give <c>pipeline use</c> a string it
    /// cannot parse.
    /// </remarks>
    [Fact]
    public void Pick_WhenAnotherRowIsChosen_ReturnsThatRowsValueAndNotItsLabel()
    {
        var console = Console(ConsoleKey.DownArrow, ConsoleKey.Enter);

        var chosen = new SpectrePipelinePicker(console)
            .Pick(Model(Choice("1.10.0", "projecta@1.10.0"), Choice("1.4.0", "projecta@1.4.0")));

        chosen.ShouldBe("projecta@1.4.0");
        chosen.ShouldNotBe("1.4.0");
    }

    /// <remarks>
    /// The active row is marked in place rather than moved to the top. Moving it
    /// would make the list order depend on machine state, and a menu whose rows
    /// sit somewhere else on a colleague's screen is a menu two people cannot
    /// talk about. The marker is asserted through the console's output because
    /// it is the one piece of rendering that carries meaning rather than style.
    /// </remarks>
    [Fact]
    public void Pick_MarksTheActiveRowInPlace()
    {
        var console = Console(ConsoleKey.Enter);

        var model = new SelectionModel(
            [Choice("1.10.0", "projecta@1.10.0"), Choice("1.4.0", "projecta@1.4.0", active: true)],
            ActiveIndex: 1,
            "Which version?");

        new SpectrePipelinePicker(console).Pick(model).ShouldBe("projecta@1.10.0");

        console.Output.ShouldContain("* 1.4.0");
        console.Output.ShouldNotContain("* 1.10.0");
    }

    /// <summary>
    /// A label carrying markup characters survives as text.
    /// </summary>
    /// <remarks>
    /// Spectre.Console reads <c>[</c> as the start of a style tag, and the
    /// labels this project builds are full of brackets — "1.4.0 (pinned,
    /// outside the range this checkout accepts)" is one parenthesis away from
    /// being one. A label that failed to escape would throw while rendering a
    /// menu, which is a crash in front of somebody who typed a correct command.
    /// </remarks>
    [Fact]
    public void Pick_WithMarkupCharactersInALabel_RendersThemAsTextAndStillMapsBack()
    {
        var console = Console(ConsoleKey.Enter);

        new SpectrePipelinePicker(console)
            .Pick(Model(Choice("1.4.0 [outside the range]", "projecta@1.4.0")))
            .ShouldBe("projecta@1.4.0");

        console.Output.ShouldContain("[outside the range]");
    }

    [Fact]
    public void Pick_WithExactlyOneRow_StillAsksRatherThanReturningWithoutAKeypress()
    {
        var console = Console(ConsoleKey.Enter);

        new SpectrePipelinePicker(console)
            .Pick(Model(Choice("1.4.0", "projecta@1.4.0")))
            .ShouldBe("projecta@1.4.0");

        // The prompt was drawn, which is what "asks" means here. A picker that
        // adopted the single row would return the same value having shown
        // nothing.
        console.Output.ShouldContain("1.4.0");
    }

    [Fact]
    public void Pick_ShowsThePromptItWasGiven()
    {
        var console = Console(ConsoleKey.Enter);

        new SpectrePipelinePicker(console).Pick(Model(Choice("1.4.0", "projecta@1.4.0")));

        console.Output.ShouldContain("Which version of 'projecta'");
    }

    [Fact]
    public void Pick_WithoutAModel_Throws() =>
        Should.Throw<ArgumentNullException>(() => new SpectrePipelinePicker(new TestConsole()).Pick(null!));

    [Fact]
    public void Constructor_WithoutAConsole_Throws() =>
        Should.Throw<ArgumentNullException>(() => new SpectrePipelinePicker((IAnsiConsole)null!));

    /// <remarks>
    /// The parameterless constructor is what <c>Program</c> uses, and it binds
    /// the picker to the real terminal. Constructing it is the whole assertion —
    /// calling <c>Pick</c> on it would block a test run waiting for a keyboard
    /// that is not there.
    /// </remarks>
    [Fact]
    public void Constructor_WithoutArguments_BindsToTheRealConsole() =>
        new SpectrePipelinePicker().ShouldNotBeNull();
}
