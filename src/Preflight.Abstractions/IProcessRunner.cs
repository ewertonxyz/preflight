namespace Preflight.Abstractions;

/// <summary>
/// Runs an external process on behalf of a rule.
/// </summary>
/// <remarks>
/// <c>core.build.compile-probe</c> is the only built-in rule that uses this,
/// and it is precisely the one that most needs to be testable without invoking
/// a real compiler.
/// </remarks>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}

public sealed record ProcessRequest
{
    public required string FileName { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? WorkingDirectory { get; init; }

    public TimeSpan? Timeout { get; init; }
}

public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration);
