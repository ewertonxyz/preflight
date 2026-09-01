namespace Preflight.Cli;

/// <summary>
/// Why this run is using the pipeline version it is using.
/// </summary>
/// <remarks>
/// <para>
/// Lives here rather than on <c>RunResult</c>, which is in
/// <c>Preflight.Core</c>. That is the arrangement <see cref="PipelineSource"/>
/// already has, and for the same reason: the version itself is a fact a machine
/// reader needs in order to tell two runs of one commit apart, so it travels on
/// the result; <em>why</em> that version was chosen is an explanation for the
/// person reading the header, and it travels beside the report the way the
/// selection source does. See ADR-029 nº10 for the original of that argument.
/// </para>
/// </remarks>
public enum PipelineVersionSource
{
    /// <summary>No package took part.</summary>
    /// <remarks>
    /// Covers both worlds in which there is nothing to say: a workspace holding
    /// its own <c>preflight.&lt;name&gt;.json</c>, and a workspace that selected
    /// no pipeline at all. Neither reaches the console header, because in
    /// neither case is there a version to name — which is what keeps the report
    /// of a run that never met a package byte-identical to the one this tool
    /// printed before packages existed.
    /// </remarks>
    None,

    /// <summary>The machine pins this version.</summary>
    Pin,

    /// <summary>No pin; the newest installed version the checkout's range accepts.</summary>
    Requirement,

    /// <summary>No pin and no range; the newest installed version.</summary>
    Newest,
}

/// <summary>
/// The installed package this run resolved to.
/// </summary>
/// <param name="Name">The pipeline name.</param>
/// <param name="Version">The version on disk.</param>
/// <param name="Root">The directory holding it.</param>
/// <param name="Source">What decided it.</param>
public sealed record InstalledPipeline(
    string Name,
    PackageVersion Version,
    DirectoryInfo Root,
    PipelineVersionSource Source);

/// <summary>
/// Lists what the install root holds.
/// </summary>
/// <remarks>
/// <para>
/// A seam local to the CLI, and deliberately not a member on
/// <c>IFileSystem</c>. That contract enumerates files, not directories, and
/// widening it is refused for the reason <c>IEnvironmentReader</c> already
/// states about itself: <c>Preflight.Abstractions</c> is a versioned contract
/// that does not grow a member because one caller needed one. A member there is
/// a minor version by <c>Docs/design.md 11.2</c>, and a member on the context
/// every plugin rule receives.
/// </para>
/// <para>
/// It is also what makes the resolution matrix testable without a disk, which
/// is the whole reason the four services of <c>Docs/design.md 5.5</c> exist at
/// all.
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
