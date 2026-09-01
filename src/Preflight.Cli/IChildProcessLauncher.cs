namespace Preflight.Cli;

/// <summary>
/// What <c>preflight measure</c> starts, and where its bytes go.
/// </summary>
/// <remarks>
/// <para>
/// Not <see cref="Preflight.Abstractions.Services.IProcessRunner"/>. That interface
/// buffers both streams into strings and returns them when the child exits,
/// which is right for a rule reading a compiler's error list and wrong for a
/// wrapper: a 38-minute build would emit nothing until it finished, and a
/// string is not a byte. Adding an overload there instead would be a
/// <b>major</b> version of <c>Preflight.Abstractions</c> recompiling every
/// plugin for a member no plugin uses.
/// </para>
/// <para>
/// It lives in <c>Preflight.Cli</c> rather than in <c>Preflight.Core</c>
/// because the criterion is whether a test project that cannot reference an
/// executable needs the concrete type. No rule streams a child process, so
/// <c>Preflight.Rules.Tests</c> does not. <c>IEnvironmentReader</c> is the
/// precedent.
/// </para>
/// </remarks>
public interface IChildProcessLauncher
{
    /// <summary>
    /// Runs <paramref name="request"/> to completion, copying its output as it
    /// arrives.
    /// </summary>
    /// <param name="request">The child.</param>
    /// <param name="standardOutput">Where the child's standard output is copied, byte for byte.</param>
    /// <param name="standardError">Where its standard error is copied, byte for byte.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The child's exit code, which the CLI then returns unchanged.</returns>
    Task<int> RunAsync(
        ChildProcessRequest request,
        Stream standardOutput,
        Stream standardError,
        CancellationToken cancellationToken);
}

/// <summary>
/// A child process to measure.
/// </summary>
/// <param name="FileName">The executable, as the user typed it.</param>
/// <param name="Arguments">Its arguments, in order, unquoted and unjoined.</param>
/// <param name="WorkingDirectory">Where it runs.</param>
public sealed record ChildProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory)
{
    /// <summary>
    /// The command line as the history records it.
    /// </summary>
    /// <remarks>
    /// For the record only. It is never parsed back into arguments — the
    /// arguments were never joined in the first place, which is what keeps
    /// quoting out of this program entirely.
    /// </remarks>
    public string Describe() => string.Join(' ', [FileName, .. Arguments]);
}
