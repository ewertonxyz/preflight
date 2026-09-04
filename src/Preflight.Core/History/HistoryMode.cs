namespace Preflight.Core.History;

/// <summary>
/// How the history files are laid out.
/// </summary>
public enum HistoryMode
{
    /// <summary>One file per month and machine. The default.</summary>
    Shared,

    /// <summary>
    /// One file per process, which removes concurrent append entirely.
    /// </summary>
    /// <remarks>
    /// The right choice when <c>historyPath</c> points at a network share,
    /// where the atomicity assumption behind a shared append is broken, and the
    /// wrong choice everywhere else, because of how many files it produces.
    /// </remarks>
    PerProcess,
}
