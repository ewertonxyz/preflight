namespace Preflight.Core.Execution;

/// <summary>
/// Why a rule was skipped.
/// </summary>
/// <remarks>
/// Lives here rather than in <c>Preflight.Abstractions</c> because no rule ever
/// produces one — only the tool does, and only the Abstractions surface is
/// versioned as a plugin contract.
/// </remarks>
public enum SkipReason
{
    DependencyFailed,
    DependencyErrored,
    DependencyDisabled,
}
