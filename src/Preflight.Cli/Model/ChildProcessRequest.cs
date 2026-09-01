namespace Preflight.Cli.Model;

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
