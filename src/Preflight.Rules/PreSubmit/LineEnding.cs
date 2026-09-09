namespace Preflight.Rules;

/// <summary>
/// The line ending a file opens with.
/// </summary>
/// <remarks>
/// Only the first one, which is the whole of what is measured. Whether a shell
/// script runs on a Linux agent is decided by the bytes before its first
/// newline, and a file with several kinds of ending is a different defect with
/// a different remedy — a normalisation pass rather than a verdict.
/// </remarks>
internal enum LineEnding
{
    /// <summary>There was no newline in the window that was read.</summary>
    None,

    /// <summary>A bare line feed.</summary>
    Lf,

    /// <summary>A carriage return followed by a line feed.</summary>
    Crlf,
}
