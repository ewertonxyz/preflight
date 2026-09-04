namespace Preflight.Cli.Policy;

using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Cli.Pipelines;
using Preflight.Core.Policy;

/// <summary>
/// The policy for one run, and everything a report needs to explain it.
/// </summary>
/// <param name="Policy">The fully resolved policy.</param>
/// <param name="Chain">
/// The files that composed it, in application order, plus the local overlay
/// when it applied.
/// </param>
/// <param name="Overlay">Whether the local overlay took part, and why.</param>
/// <param name="Selection">Which pipeline was used, and what decided it.</param>
/// <param name="Package">
/// The installed package the policy came from, or <see langword="null"/> when
/// none took part.
/// </param>
public sealed record ResolvedPolicy(
    EffectivePolicy Policy,
    IReadOnlyList<string> Chain,
    LocalOverlayDecision Overlay,
    PipelineSelection Selection,
    InstalledPipeline? Package = null);
