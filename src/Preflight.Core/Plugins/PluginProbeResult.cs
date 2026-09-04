namespace Preflight.Core.Plugins;

/// <summary>
/// Which assemblies a run will probe, and which of the given paths were
/// unusable.
/// </summary>
/// <remarks>
/// The two travel together because a caller that took the paths and dropped the
/// errors would produce exactly the outcome this refuses: a run that finished
/// without the plugins somebody declared, and said nothing.
/// </remarks>
public sealed record PluginProbeResult(
    IReadOnlyList<string> AssemblyPaths,
    IReadOnlyList<PluginLoadError> Errors);
