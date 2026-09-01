namespace Preflight.Cli.Policy;

/// <summary>
/// Whether <c>preflight.local.json</c> takes part in this run, and why.
/// </summary>
/// <param name="Applied">
/// <see langword="true"/> when the local overlay is merged into the effective
/// policy.
/// </param>
/// <param name="CiVariable">
/// The CI variable that was detected, or <see langword="null"/> if none was.
/// Set whenever CI was detected, <em>including</em> when <c>--allow-local</c>
/// overrode it — the console header and the <c>explain</c> line both name the
/// variable, and a run that forced the overlay on inside CI is exactly the run
/// where naming it matters.
/// </param>
/// <param name="Suppressed">
/// Why the overlay is not applied, or
/// <see cref="LocalOverlaySuppression.None"/> when it is.
/// </param>
public sealed record LocalOverlayDecision(
    bool Applied,
    string? CiVariable,
    LocalOverlaySuppression Suppressed);
