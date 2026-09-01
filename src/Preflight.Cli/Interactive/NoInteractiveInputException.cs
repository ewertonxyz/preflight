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
