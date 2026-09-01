namespace Preflight.Cli.Storage;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Preflight.Core.Policy;

/// <summary>
/// Raised when the machine state file exists and cannot be understood.
/// </summary>
/// <remarks>
/// A configuration error, so it exits 2 through the one mapping that decides
/// exit codes. The machine's own state being unreadable is a condition of the
/// machine, not a defect in this tool.
/// </remarks>
public sealed class MachineStateException : Preflight.Core.ConfigurationLoadException
{
    public MachineStateException(string message)
        : base(message)
    {
    }
}
