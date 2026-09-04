namespace Preflight.Cli.Pipelines;

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
