namespace Preflight.Core.Execution;

/// <summary>
/// The outcome of a whole run.
/// </summary>
public enum RunVerdict
{
    Passed,
    PassedWithWarnings,
    Blocked,
    Errored,
}
