namespace Preflight.Abstractions.Services;

/// <summary>
/// One process a rule asks <see cref="IProcessRunner"/> to run.
/// </summary>
/// <remarks>
/// <see cref="Arguments"/> is a list rather than a single command line, so that
/// an argument containing a space is passed as one argument on every platform
/// instead of depending on the caller having quoted it correctly.
/// </remarks>
public sealed record ProcessRequest
{
    public required string FileName { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? WorkingDirectory { get; init; }

    public TimeSpan? Timeout { get; init; }
}
