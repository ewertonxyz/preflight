namespace Preflight.Cli.Commands;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core;
using Preflight.Rules;

/// <summary>
/// Raised when the file <c>create</c> would write already exists.
/// </summary>
/// <remarks>
/// A configuration error, so it exits 2 through the one mapping that decides
/// exit codes. 3 would say the tool broke; the tool did exactly what it
/// promised.
/// </remarks>
public sealed class WorkspaceFileExistsException : ConfigurationLoadException
{
    public WorkspaceFileExistsException(string message)
        : base(message)
    {
    }
}
