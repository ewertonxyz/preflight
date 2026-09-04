namespace Preflight.Core.Execution;

/// <summary>
/// Thrown when a type that declared itself a rule cannot be turned into one.
/// </summary>
/// <remarks>
/// A <see cref="ConfigurationLoadException"/> rather than a run outcome, on
/// purpose. A load failure is exit 2, and the distinction is worth keeping: a
/// broken configuration calls the tool's owner, a failing check calls the
/// commit author.
/// </remarks>
public sealed class RuleDiscoveryException : ConfigurationLoadException
{
    public RuleDiscoveryException(string message)
        : base(message)
    {
    }
}
