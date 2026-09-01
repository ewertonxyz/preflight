namespace Preflight.Cli.Interactive;

using Preflight.Cli.Commands;
using Preflight.Core;

/// <summary>
/// Asks the person at the keyboard.
/// </summary>
/// <remarks>
/// Injected, so that every test drives the commands without a terminal, and so
/// that the refusal path is the one the tests exercise rather than the one
/// nobody can reach. The implementation renders; nothing else does.
/// </remarks>
public interface IPipelinePicker
{
    /// <summary>Shows <paramref name="model"/> and returns the chosen value.</summary>
    /// <param name="model">What to show.</param>
    string Pick(SelectionModel model);
}
