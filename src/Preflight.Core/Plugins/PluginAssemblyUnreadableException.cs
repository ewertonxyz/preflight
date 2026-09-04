namespace Preflight.Core.Plugins;

/// <summary>
/// Thrown when a file in a plugin directory will not open as an assembly.
/// </summary>
/// <remarks>
/// A <see cref="ConfigurationLoadException"/> so that an instance escaping the
/// accumulation in <see cref="PluginLoader"/> still reaches exit 2 rather than
/// exit 3. That difference decides who gets called, and a broken DLL is the
/// tool owner's problem down either path.
/// </remarks>
public sealed class PluginAssemblyUnreadableException : ConfigurationLoadException
{
    public PluginAssemblyUnreadableException(string message)
        : base(message)
    {
    }
}
