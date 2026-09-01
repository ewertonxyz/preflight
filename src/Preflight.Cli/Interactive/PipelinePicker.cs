namespace Preflight.Cli.Interactive;

using Preflight.Cli.Commands;
using Preflight.Cli.Policy;
using Preflight.Core;

/// <summary>
/// The gate every interactive path goes through.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IPipelinePicker"/> because a refusal that lives
/// inside the thing a test replaces is a refusal no test exercises. No
/// interactive stdin, or a detected CI, is exit 2 — never a prompt that falls
/// back to a default, which is a selection nobody made deciding what gets
/// validated. The pipeline name refuses the same inference one floor up: a
/// single plausible answer is still not an answer anybody gave.
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
