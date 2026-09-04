namespace Preflight.Core.History;

using Preflight.Core.Execution;
using Preflight.Core.Policy;

/// <summary>
/// Appends the two kinds of event to the history.
/// </summary>
/// <remarks>
/// Composition only: which file (<see cref="HistoryPaths"/>), what the line
/// says (<see cref="HistoryLine"/>) and how it lands
/// (<see cref="IHistoryStore"/>) are each somebody else's decision. What is
/// left here is the part that has to be the same for both event types, which is
/// exactly the part that would otherwise be written twice and drift once.
/// </remarks>
public sealed class NdjsonHistoryWriter
{
    private readonly IHistoryStore _store;
    private readonly EngineEnvironment _machine;
    private readonly TimeProvider _clock;

    /// <param name="store">Where a line is appended.</param>
    /// <param name="machine">Names the file.</param>
    /// <param name="clock">Decides which month the file belongs to.</param>
    public NdjsonHistoryWriter(IHistoryStore store, EngineEnvironment machine, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _machine = machine;
        _clock = clock;
    }

    /// <summary>Records one run.</summary>
    public Task WriteRunAsync(
        DirectoryInfo workspaceRoot,
        HistorySettings settings,
        RunResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(settings);

        return AppendAsync(workspaceRoot, settings, HistoryLine.ForRun(result), cancellationToken);
    }

    /// <summary>Records one measured child process.</summary>
    public Task WriteExternalAsync(
        DirectoryInfo workspaceRoot,
        HistorySettings settings,
        ExternalMeasurement measurement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(settings);

        return AppendAsync(workspaceRoot, settings, HistoryLine.ForExternal(measurement), cancellationToken);
    }

    private Task AppendAsync(
        DirectoryInfo workspaceRoot,
        HistorySettings settings,
        string line,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            HistoryPaths.DirectoryFor(workspaceRoot, settings),
            HistoryPaths.FileNameFor(settings, _machine, _clock.GetUtcNow()));

        return _store.AppendAsync(path, line, cancellationToken);
    }
}
