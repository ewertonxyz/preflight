namespace Preflight.Core.History;

/// <summary>
/// One line of the history, whether or not it could be understood.
/// </summary>
/// <remarks>
/// The unreadable and ignored shapes are the whole reason this type exists
/// rather than a bare <see cref="HistoryEvent"/>. Two processes appending to
/// one file on a network share can interleave a line, and a reader that
/// silently swallowed one would let the report publish percentiles over an
/// unknown fraction of the sample. That is the same defect as a green run over
/// a check that never ran, aimed at the instrumentation instead of at the
/// workspace: the number looks like a measurement and is not one.
/// </remarks>
public abstract record HistoryEntry
{
    private HistoryEntry()
    {
    }

    /// <summary>A line that was understood.</summary>
    /// <param name="Value">The event it carried.</param>
    public sealed record Parsed(HistoryEvent Value) : HistoryEntry;

    /// <summary>A line that could not be understood, and is counted as such.</summary>
    /// <param name="File">The file it was in.</param>
    /// <param name="Line">Its one-based position in that file.</param>
    public sealed record Unreadable(string File, int Line) : HistoryEntry;

    /// <summary>
    /// A well-formed line naming an event type this version does not know.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Unreadable"/> because it means something
    /// different: not damage, but a newer or a future writer. It is what lets a
    /// later phase add an event type without invalidating the history already
    /// on disk.
    /// </remarks>
    /// <param name="Type">The <c>type</c> it declared.</param>
    public sealed record Ignored(string Type) : HistoryEntry;
}
