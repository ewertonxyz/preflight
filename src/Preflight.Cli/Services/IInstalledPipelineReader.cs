namespace Preflight.Cli.Services;

using Preflight.Cli.Pipelines;

/// <summary>
/// Lists what the install root holds.
/// </summary>
/// <remarks>
/// <para>
/// Injected, local to the CLI, and deliberately not a member on
/// <c>IFileSystem</c>. That contract enumerates files, not directories, and
/// widening it is refused for the reason <c>IEnvironmentReader</c> already
/// states about itself: <c>Preflight.Abstractions</c> is a versioned contract
/// that does not grow a member because one caller needed one. Below 1.0 the
/// minor is the breaking axis, so a member there is a version every compiled
/// plugin has to be rebuilt against — and a member on the context every plugin
/// rule receives.
/// </para>
/// <para>
/// It is also what makes the resolution matrix testable without a disk, which
/// is why the services a rule depends on are injected in the first place.
/// </para>
/// </remarks>
public interface IInstalledPipelineReader
{
    /// <summary>
    /// The versions installed for <paramref name="pipeline"/>, in ascending order.
    /// </summary>
    /// <remarks>
    /// A directory whose name is not a package version is skipped rather than
    /// raised: the install root is a place a person can open in a file manager,
    /// and a stray folder there must not stop a run that was never going to read
    /// it.
    /// </remarks>
    /// <param name="pipeline">The pipeline name.</param>
    IReadOnlyList<PackageVersion> Versions(string pipeline);

    /// <summary>The names that have at least one version installed, in ordinal order.</summary>
    IReadOnlyList<string> Pipelines();

    /// <summary>Reads one installed package's manifest.</summary>
    /// <param name="pipeline">The pipeline name.</param>
    /// <param name="version">The installed version.</param>
    PackageManifest Manifest(string pipeline, PackageVersion version);
}
