namespace Preflight.Core.Plugins;

/// <summary>
/// Opens a plugin assembly and describes what is in it.
/// </summary>
/// <remarks>
/// <para>
/// One method, because there is exactly one thing about plugin loading that
/// needs a real file on a real disk. Everything else loading decides — which
/// versions are compatible, which types are rules, which ids collide, what a
/// refusal says — is decided from a
/// <see cref="LoadedPluginAssembly"/> and therefore from data a test can write
/// down.
/// </para>
/// <para>
/// <see cref="IDisposable"/> rather than a per-assembly handle. Each assembly
/// gets its own collectible load context, and a caller that has to remember to
/// release each one individually will eventually not: the loader owns every
/// context it opened, including the ones opened before the assembly that
/// failed, which is the case where a leak would otherwise be invisible.
/// </para>
/// </remarks>
public interface IAssemblyLoader : IDisposable
{
    /// <summary>
    /// Loads the assembly at <paramref name="assemblyPath"/>.
    /// </summary>
    /// <exception cref="PluginAssemblyUnreadableException">
    /// The file is not a readable assembly, or a dependency it needs is
    /// missing.
    /// </exception>
    LoadedPluginAssembly Load(string assemblyPath);
}
