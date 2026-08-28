namespace Preflight.Core.Tests.History;

using Preflight.Core.History;

/// <summary>
/// An <see cref="IHistoryStore"/> that keeps what it was asked to append.
/// </summary>
/// <remarks>
/// A class rather than a substitute, because every writer test asserts both the
/// path and the line and a substitute would need argument capture for each.
/// The optional failure is how the CLI tests reach the history format's rule that a
/// history that cannot be written does not change the verdict.
/// </remarks>
public sealed class RecordingHistoryStore : IHistoryStore
{
    private readonly Exception? _failure;

    public RecordingHistoryStore(Exception? failure = null)
    {
        _failure = failure;
    }

    public List<(string Path, string Line)> Appended { get; } = [];

    public Task AppendAsync(string filePath, string line, CancellationToken cancellationToken)
    {
        if (_failure is not null)
        {
            return Task.FromException(_failure);
        }

        Appended.Add((filePath, line));

        return Task.CompletedTask;
    }
}
