namespace Preflight.Cli.Pipelines;

using Preflight.Abstractions.Services;
using Preflight.Core.Policy;

/// <summary>
/// Which pipeline a run uses, and how that was decided.
/// </summary>
/// <param name="Pipeline">The name, or <see langword="null"/> when there is none.</param>
/// <param name="Source">What decided it.</param>
public sealed record PipelineSelection(string? Pipeline, PipelineSource Source)
{
    /// <summary>No pipeline, chosen by nobody.</summary>
    /// <remarks>
    /// A value rather than a null, so that a command which never selects one —
    /// every subcommand of <c>pipeline</c> — carries the same type as a command
    /// that did. Null would make every reader ask whether it means "none" or
    /// "not resolved yet", and those are the same thing here.
    /// </remarks>
    public static PipelineSelection None { get; } = new(null, PipelineSource.None);
}
