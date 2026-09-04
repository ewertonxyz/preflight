namespace Preflight.Core.Plugins;

/// <summary>
/// Thrown when loading the plugin set finds one or more defects.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="ConfigurationLoadException"/>, so exit 2 is reached through the
/// boundary every other load-time failure already uses rather than through a
/// second <c>catch</c> somebody has to remember.
/// </para>
/// <para>
/// Accumulates, matching policy and graph validation. Someone who pointed
/// <c>--rules-path</c> at a directory of four plugins built against last
/// quarter's contract should be told about four of them, not asked to run the
/// tool four times.
/// </para>
/// </remarks>
public sealed class PluginLoadException : ConfigurationLoadException
{
    public PluginLoadException(IReadOnlyList<PluginLoadError> errors)
        : base(string.Join(Environment.NewLine, errors.Select(error => error.Message)))
    {
        Errors = errors;
    }

    public IReadOnlyList<PluginLoadError> Errors { get; }
}
