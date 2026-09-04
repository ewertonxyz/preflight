namespace Preflight.Core.Execution;

/// <summary>
/// A child process could not be started.
/// </summary>
/// <remarks>
/// A <see cref="ConfigurationLoadException"/> so it lands on exit 2. A missing
/// git or a missing compiler is something the person running the tool installs,
/// not a defect in the tool, and that distinction decides who gets called.
/// </remarks>
public sealed class ProcessLaunchException : ConfigurationLoadException
{
    public ProcessLaunchException(string message)
        : base(message)
    {
    }
}
