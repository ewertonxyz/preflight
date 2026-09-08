namespace Preflight.Abstractions.Services;

using Preflight.Abstractions.Model;

/// <summary>
/// Reads how much room the volume holding a path has left.
/// </summary>
/// <remarks>
/// <para>
/// A separate interface rather than a method on the file system, because a
/// member added to an interface every rule already implements breaks every
/// compiled plugin, while a new type breaks none. The cache contract took the
/// same shape for the same reason.
/// </para>
/// <para>
/// Read-only, like the file system beside it. A rule never changes the
/// workspace it is judging, and an interface that cannot express a write says
/// so in the type system rather than in a comment somebody has to find.
/// </para>
/// </remarks>
public interface IVolumeProbe
{
    /// <summary>
    /// Measures the volume that holds <paramref name="path"/>.
    /// </summary>
    /// <param name="path">An absolute path on the volume to measure.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>
    /// What the volume holds, or <see langword="null"/> when it cannot be
    /// measured — the path is not there, or the platform will not answer.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Null rather than an exception or a zero. Zero is a measurement and would
    /// be read as a full disk; an exception would turn "this path is not on a
    /// volume this host can describe" into a defect of the rule. Null is the
    /// third answer, and the rule that receives it reports that it checked
    /// nothing rather than inventing a verdict.
    /// </para>
    /// <para>
    /// Asynchronous and cancellable even though measuring a local volume is a
    /// syscall, because the path measured is whatever the workspace declared
    /// and a declared path can sit on a network share. A dead share blocks
    /// until the operating system gives up, and a synchronous call there turns
    /// Ctrl+C into a hang — with the partial history record, which exists
    /// precisely for an interrupted run, never written.
    /// </para>
    /// </remarks>
    Task<VolumeSpace?> ProbeAsync(string path, CancellationToken cancellationToken);
}
