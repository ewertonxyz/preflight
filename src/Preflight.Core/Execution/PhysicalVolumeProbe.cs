namespace Preflight.Core.Execution;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Services;

/// <summary>
/// The volume probe the tool ships, reading the real filesystem.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than in the command line for the same reason the file system and
/// the process runner are: the integration layer of the rule tests needs the
/// shipped implementation, and a test project cannot reference an executable.
/// </para>
/// <para>
/// Deliberately thin, and every way of failing answers <see langword="null"/>
/// rather than throwing. A probe that threw would turn "this host cannot
/// describe that volume" into an errored rule, which reads as a defect in the
/// tool rather than as a measurement nobody could take.
/// </para>
/// </remarks>
public sealed class PhysicalVolumeProbe : IVolumeProbe
{
    /// <inheritdoc/>
    public Task<VolumeSpace?> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Measure(path));
    }

    /// <remarks>
    /// One <c>try</c> and no guards in front of it. A path that is not rooted, a
    /// drive letter nothing is mounted on, a removable drive with no media in
    /// it, and a share this account may not query all fail somewhere inside
    /// these three lines, and every one of them means the same thing: no
    /// measurement was taken. Testing for each in advance would restate what
    /// the failure already says, and two of those tests could only be reached
    /// on hardware in a particular state — which is how a branch ends up
    /// permanently uncovered and then quietly excluded.
    /// </remarks>
    private static VolumeSpace? Measure(string path)
    {
        try
        {
            // The span overload, because the string one is annotated nullable
            // and the null it describes cannot arrive here — the guard above
            // rejects it. Taking the span leaves no arm for a null, and a
            // relative path yields an empty root that DriveInfo refuses in the
            // ordinary way, along with everything else that cannot be measured.
            var drive = new DriveInfo(new string(Path.GetPathRoot(path.AsSpan())));

            // The name the operating system gives the volume, never the path
            // that was probed. The probed path carries the account directory on
            // a developer's machine, and this value reaches the console report
            // and the run's stored history.
            return new VolumeSpace(drive.Name, drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
