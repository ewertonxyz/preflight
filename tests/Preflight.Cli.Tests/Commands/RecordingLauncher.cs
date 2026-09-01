namespace Preflight.Cli.Tests.Commands;

using Preflight.Cli.Model;
using Preflight.Cli.Services;

/// <summary>
/// A child process that never runs, and remembers being asked to.
/// </summary>
/// <remarks>
/// <see cref="Started"/> is the assertion that matters for the two refusals of
/// <c>--label</c> and the command itself are checked <em>before</em>
/// anything is started, which is what makes 2 and 127 mean different things.
/// A test asserting only the exit code would pass against an implementation
/// that started the child and then changed its mind.
/// </remarks>
public sealed class RecordingLauncher : IChildProcessLauncher
{
    public int ExitCode { get; init; }

    public Exception? Failure { get; init; }

    public byte[] StandardOutput { get; init; } = [];

    public byte[] StandardError { get; init; } = [];

    public bool Started { get; private set; }

    public ChildProcessRequest? Request { get; private set; }

    public async Task<int> RunAsync(
        ChildProcessRequest request,
        Stream standardOutput,
        Stream standardError,
        CancellationToken cancellationToken)
    {
        Started = true;
        Request = request;

        if (Failure is not null)
        {
            throw Failure;
        }

        await standardOutput.WriteAsync(StandardOutput, cancellationToken);
        await standardError.WriteAsync(StandardError, cancellationToken);

        return ExitCode;
    }
}

/// <summary>
/// A history store that refuses to write.
/// </summary>
/// <remarks>
/// The history format: a failure here warns on standard error and changes nothing
/// else. The exception types are the two ways a real disk says no, taken from
/// what <c>FileHistoryStore</c> actually throws.
/// </remarks>
public sealed class FailingHistoryStore : Preflight.Core.History.IHistoryStore
{
    public FailingHistoryStore(Exception failure)
    {
        Failure = failure;
    }

    public Exception Failure { get; }

    public Task AppendAsync(string filePath, string line, CancellationToken cancellationToken) =>
        Task.FromException(Failure);
}

/// <summary>
/// A cache that refuses to read or write.
/// </summary>
/// <remarks>
/// The cache is an optimisation, and an optimisation that can change a verdict
/// has traded away the thing the tool is for. This is the fixture that proves it
/// cannot, and it is the sibling of <see cref="FailingHistoryStore"/> for the
/// same reason the history format gives about the history.
/// </remarks>
public sealed class FailingRuleCacheStore : Preflight.Core.Caching.IRuleCacheStore
{
    public FailingRuleCacheStore(Exception failure)
    {
        Failure = failure;
    }

    public Exception Failure { get; }

    public Task<string?> ReadAsync(string filePath, CancellationToken cancellationToken) =>
        Task.FromException<string?>(Failure);

    public Task WriteAsync(string filePath, string content, CancellationToken cancellationToken) =>
        Task.FromException(Failure);

    public int Clear(string directory) => throw Failure;
}
