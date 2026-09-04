namespace Preflight.Cli.Commands;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Preflight.Core;

/// <summary>
/// Raised when a source tree cannot be packed, or the output cannot be written.
/// </summary>
public sealed class PipelinePackException : ConfigurationLoadException
{
    public PipelinePackException(string message)
        : base(message)
    {
    }
}
