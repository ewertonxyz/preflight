namespace Preflight.Core.Policy;

using Preflight.Abstractions.Model;

/// <summary>
/// The target of a run, separating what the user said from what defaulted.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BuildTarget"/> is still the effective target and is not
/// duplicated here. What it cannot answer is the question this layer depends
/// on: <c>--platform</c> falls back to <c>any</c> and <c>--configuration</c> to
/// <c>Development</c>, so a <c>targets</c> block matching those defaults would
/// apply one platform's thresholds to every run that forgot the flag.
/// </para>
/// <para>
/// The rule the command line follows everywhere else is to refuse rather than
/// assume: an invocation that omits what it needs is a configuration error and
/// not an opportunity for a convenient default, because a default runs
/// something nobody asked for and then reports success over it. Matching a
/// <c>targets</c> block against the effective target would break exactly that
/// rule — both defaults predate the feature, so a block would begin selecting
/// policy on runs that never named an axis. What makes a match safe is that
/// the axis was typed, not that the value it defaulted to looks respectable.
/// </para>
/// </remarks>
/// <param name="Effective">The target the rules receive.</param>
/// <param name="PlatformStated">Whether <c>--platform</c> was given.</param>
/// <param name="ConfigurationStated">Whether <c>--configuration</c> was given.</param>
public sealed record StatedBuildTarget(
    BuildTarget Effective,
    bool PlatformStated,
    bool ConfigurationStated)
{
    /// <summary>A run that stated neither axis.</summary>
    /// <remarks>
    /// The default for every caller that does not care — a policy read for a
    /// command that names no target still has to build one, and this says
    /// plainly that no <c>targets</c> block can match it.
    /// </remarks>
    public static StatedBuildTarget Unstated { get; } =
        new(new BuildTarget("any", "Development"), PlatformStated: false, ConfigurationStated: false);
}
