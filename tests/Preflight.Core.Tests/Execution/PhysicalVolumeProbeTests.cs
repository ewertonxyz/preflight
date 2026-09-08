namespace Preflight.Core.Tests.Execution;

using Preflight.Core.Execution;

/// <summary>
/// Fixes <see cref="PhysicalVolumeProbe"/> against a real disk.
/// </summary>
/// <remarks>
/// The integration layer for the one implementation the tool ships. Every rule
/// test substitutes the probe, which proves the rules behave as specified and
/// proves nothing about whether the shipped probe reaches a volume at all.
/// </remarks>
public sealed class PhysicalVolumeProbeTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("preflight-volume-tests");

    private readonly PhysicalVolumeProbe _probe = new();

    public void Dispose() => _directory.Delete(recursive: true);

    /// <remarks>
    /// The only assertion in the suite that proves the shipped probe reaches a
    /// disk. The numbers are compared against nothing in particular on purpose:
    /// a threshold would be a fact about the machine running the tests.
    /// </remarks>
    [Fact]
    public async Task ProbeAsync_ForARealDirectory_ReturnsAVolumeWithPositiveTotalBytes()
    {
        var space = await _probe.ProbeAsync(_directory.FullName, CancellationToken.None);

        space.ShouldNotBeNull();
        space.TotalBytes.ShouldBeGreaterThan(0);
        space.AvailableBytes.ShouldBeGreaterThanOrEqualTo(0);
        space.VolumeName.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A volume that is not there answers with nothing, and does not throw.
    /// </summary>
    /// <remarks>
    /// A probe that threw would turn "this host cannot describe that volume"
    /// into an errored rule, which reads as a defect in the tool rather than as
    /// a measurement nobody could take — and the rule branches on the null,
    /// reporting that it checked nothing.
    /// </remarks>
    [Fact]
    public async Task ProbeAsync_ForAPathOnAVolumeThatIsNotThere_ReturnsNull()
    {
        var absent = OperatingSystem.IsWindows()
            ? @"Q:\no\such\place"
            : "/no/such/mount/point";

        var space = await _probe.ProbeAsync(absent, CancellationToken.None);

        space.ShouldBeNull();
    }

    /// <summary>
    /// The volume is named by the operating system, never by the path probed.
    /// </summary>
    /// <remarks>
    /// This value reaches the console report, the build log and the run's
    /// stored history. The path handed in carries the account directory of
    /// whoever ran the tool, and echoing it back would publish that to everyone
    /// who can read a build.
    /// </remarks>
    [Fact]
    public async Task ProbeAsync_NamesTheVolumeWithoutTheAccountPath()
    {
        var space = await _probe.ProbeAsync(_directory.FullName, CancellationToken.None);

        space.ShouldNotBeNull();
        space.VolumeName.ShouldNotContain(_directory.Name);
        space.VolumeName.ShouldNotContain(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Case.Insensitive);
    }

    /// <remarks>
    /// A path whose root names no volume at all, as distinct from a drive letter
    /// nothing is mounted on. Both answer with nothing, and neither is allowed
    /// to escape as an exception.
    /// </remarks>
    [Fact]
    public async Task ProbeAsync_ForAPathWithNoVolumeInItsRoot_ReturnsNull()
    {
        (await _probe.ProbeAsync("relative/path/with/no/root", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task ProbeAsync_WithACancelledToken_StopsRatherThanMeasuring()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _probe.ProbeAsync(_directory.FullName, cancellation.Token));
    }
}
