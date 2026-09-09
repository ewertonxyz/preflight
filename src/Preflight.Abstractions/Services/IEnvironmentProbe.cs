namespace Preflight.Abstractions.Services;

/// <summary>
/// Reads the environment block the tool was started with.
/// </summary>
/// <remarks>
/// <para>
/// A separate interface rather than a member on the file system or on the
/// volume probe beside it. A member added to an interface every plugin already
/// implements breaks every compiled plugin, while a new type breaks none — and
/// measuring a disk and reading an environment are not one responsibility, so a
/// host able to offer one would have to fake the other.
/// </para>
/// <para>
/// Synchronous and without a cancellation token, where
/// <see cref="IVolumeProbe"/> is neither. The asymmetry is the point: a volume
/// probe measures whatever path the workspace declared, and a declared path can
/// sit on a dead network share that blocks until the operating system gives up.
/// An environment block is a dictionary this process was handed at start-up.
/// There is nothing to wait for, and an asynchronous signature would promise a
/// cancellation that can never arrive.
/// </para>
/// <para>
/// Read-only, like everything else a rule receives. A rule able to set a
/// variable would change the environment of every rule running beside it, and
/// rules at one level of the graph run concurrently.
/// </para>
/// </remarks>
public interface IEnvironmentProbe
{
    /// <summary>
    /// Reads one variable.
    /// </summary>
    /// <param name="name">The variable to read.</param>
    /// <returns>
    /// Its value, or <see langword="null"/> when it is not set.
    /// </returns>
    /// <remarks>
    /// Null for "not set" rather than an empty string, because the two are
    /// different facts and only one of them is something somebody did on
    /// purpose. Never throws: a name this process cannot read is not set, as
    /// far as anything downstream of it is concerned.
    /// </remarks>
    string? Read(string name);
}
