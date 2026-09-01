namespace Preflight.Cli.Interactive;

using Preflight.Cli.Commands;
using Preflight.Core;

/// <summary>
/// Raised when there is nobody at the keyboard to answer.
/// </summary>
/// <remarks>
/// A configuration error, so it exits 2 through the one mapping that decides
/// exit codes. 3 would say the tool broke; a redirected stdin is the
/// invocation's shape, not a defect here.
/// </remarks>
public sealed class NoInteractiveInputException : ConfigurationLoadException
{
    public NoInteractiveInputException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Asks the person at the keyboard.
/// </summary>
/// <remarks>
/// A seam, so that every test drives the commands without a terminal, and so
/// that the refusal path is the one the tests exercise rather than the one
/// nobody can reach. The implementation renders; nothing else does. See
/// ADR-035.
/// </remarks>
public interface IPipelinePicker
{
    /// <summary>Shows <paramref name="model"/> and returns the chosen value.</summary>
    /// <param name="model">What to show.</param>
    string Pick(SelectionModel model);
}

/// <summary>
/// The gate every interactive path goes through.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IPipelinePicker"/> because the refusals are the
/// part worth being sure about and they must not live inside the thing tests
/// replace. No interactive stdin, or a detected CI, is exit 2 — never a prompt
/// that falls back to a default, which is a selection nobody made deciding what
/// gets validated. ADR-029 spent a whole decision refusing exactly that, one
/// floor up.
/// </para>
/// <para>
/// A single candidate is still shown rather than adopted. The pipeline selector
/// already made this call about a workspace holding one <c>preflight.*.json</c>
/// — "one candidate is not a choice anybody made" — and adopting one here would
/// be that same silence wearing a menu's clothes.
/// </para>
/// </remarks>
public static class PipelinePicker
{
    /// <summary>
    /// Asks, or refuses because nobody can answer.
    /// </summary>
    /// <param name="environment">Where the console and the CI variables are.</param>
    /// <param name="model">What to show.</param>
    /// <exception cref="NoInteractiveInputException">
    /// Input is redirected, CI was detected, or there is nothing to choose from.
    /// </exception>
    public static string Choose(CommandEnvironment environment, SelectionModel model)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(model);

        if (model.Choices.Count == 0)
        {
            throw new NoInteractiveInputException(
                $"{model.Prompt} Nothing is installed to choose from. " +
                "Install a pipeline package first: preflight pipeline install <package>.");
        }

        // stdin, not stdout. `echo 1 | preflight pipeline use` has an
        // interactive stdout and no keyboard behind it, and asking the wrong
        // half of the console produces a prompt that reads a byte somebody's
        // script meant for something else.
        if (!environment.Console.IsInputInteractive)
        {
            throw new NoInteractiveInputException(
                $"{model.Prompt} There is no interactive input to ask on, so nothing was chosen. " +
                $"Name it on the command line instead: {Choices(model)}.");
        }

        if (LocalOverlay.DetectCi(environment.Environment) is { } variable)
        {
            throw new NoInteractiveInputException(
                $"{model.Prompt} CI detected: {variable}. A build agent has nobody to ask, " +
                $"so nothing was chosen. Name it on the command line instead: {Choices(model)}.");
        }

        return environment.Picker.Pick(model);
    }

    private static string Choices(SelectionModel model) =>
        string.Join(", ", model.Choices.Select(choice => choice.Value));
}
