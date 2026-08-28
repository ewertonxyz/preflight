namespace Preflight.Abstractions.Services;

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
