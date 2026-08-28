namespace Preflight.TestSupport;

using Preflight.Core.Plugins;

/// <summary>
/// An <see cref="IAssemblyLoader"/> that answers from a table.
/// </summary>
/// <remarks>
/// The seam that makes the loader's decisions testable without a DLL per case.
/// What it fakes is narrow on purpose — a path either produces an assembly
/// description or throws the one exception the real loader throws — because
/// everything else about plugin loading is a judgement about that description,
/// and a judgement is what the tests are for.
/// </remarks>
public sealed class FakeAssemblyLoader : IAssemblyLoader
{
    private readonly Dictionary<string, LoadedPluginAssembly> _assemblies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _failures = new(StringComparer.Ordinal);

    /// <summary>How many times the caller released this loader.</summary>
    public int Disposals { get; private set; }

    public FakeAssemblyLoader Containing(LoadedPluginAssembly assembly)
    {
        _assemblies[assembly.Path] = assembly;

        return this;
    }

    public FakeAssemblyLoader Failing(string path, string reason)
    {
        _failures[path] = reason;

        return this;
    }

    public LoadedPluginAssembly Load(string assemblyPath) =>
        _failures.TryGetValue(assemblyPath, out var reason)
            ? throw new PluginAssemblyUnreadableException(reason)
            : _assemblies[assemblyPath];

    public void Dispose() => Disposals++;
}
