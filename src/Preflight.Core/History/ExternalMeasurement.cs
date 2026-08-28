namespace Preflight.Core.History;

/// <summary>
/// One <c>external</c> event in the history: a child process
/// <c>preflight measure</c> timed.
/// </summary>
/// <remarks>
/// This is how the real duration of a build enters the history — measured, not
/// stated, and it is the whole argument for the command existing: a comparison
/// against a build time somebody remembered is not a comparison.
/// </remarks>
/// <param name="Label">The <c>--label</c> the invocation carried.</param>
/// <param name="StartedAt">When the child was started.</param>
/// <param name="Duration">How long it took.</param>
/// <param name="ExitCode">What it returned, which the CLI then returns too.</param>
/// <param name="Command">The child and its arguments, for the record.</param>
public sealed record ExternalMeasurement(
    string Label,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    int ExitCode,
    string Command);
