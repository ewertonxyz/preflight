namespace Preflight.Cli.Commands;

using Preflight.Cli.Interactive;
using Preflight.Core;

/// <summary>
/// Raised when a pipeline command is asked for something it will not do.
/// </summary>
public sealed class PipelineCommandException : ConfigurationLoadException
{
    public PipelineCommandException(string message)
        : base(message)
    {
    }
}
