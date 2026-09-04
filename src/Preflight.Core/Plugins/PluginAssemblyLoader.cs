namespace Preflight.Core.Plugins;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

/// <summary>
/// Loads each plugin assembly into a collectible context of its own.
/// </summary>
/// <remarks>
/// <para>
/// One collectible
/// <see cref="AssemblyLoadContext"/> per assembly, and
/// <c>Preflight.Abstractions</c> resolved by delegating to the default context
/// rather than by loading a second copy.
/// </para>
/// <para>
/// The delegation is the whole point and the reason this class exists at all.
/// Without it, the <c>IValidationRule</c> a plugin implements is a different
/// type from the one the engine knows, <c>IsAssignableFrom</c> is false, and
/// the rule is discarded in silence. It is one of the most irritating bugs in
/// .NET plugin systems, and the reason it is irritating is that everything
/// looks fine.
/// </para>
/// </remarks>
public sealed class PluginAssemblyLoader : IAssemblyLoader
{
    private readonly List<PluginLoadContext> _contexts = [];

    public LoadedPluginAssembly Load(string assemblyPath)
    {
        ArgumentNullException.ThrowIfNull(assemblyPath);

        Assembly assembly;

        try
        {
            var context = new PluginLoadContext(assemblyPath);

            // Registered before the load is attempted, not after. A context that
            // opened a file and then threw is still a context holding it open,
            // and the caller has no other handle on it to release.
            _contexts.Add(context);

            assembly = context.LoadFromAssemblyPath(assemblyPath);
        }
        catch (SystemException exception)
        {
            // One clause, and the base class is the point of it.
            //
            // Three different types arrive here from two different calls, and
            // every one of them means the same thing to the caller — this file
            // did not become an assembly, so the run stops with exit 2 naming
            // it. BadImageFormatException is a file that is not managed code.
            // FileLoadException and FileNotFoundException are a file that will
            // not open and one that went away after the probe.
            // InvalidOperationException is AssemblyDependencyResolver refusing,
            // in its constructor, a component path it cannot locate — before
            // LoadFromAssemblyPath is ever reached.
            //
            // Written as three named clauses first, which left one of them
            // unreachable: the resolver rejects a missing file before the load
            // does, and a locked file cannot be produced portably. Their shared
            // base removes the dead clause instead of excluding it, which is
            // this project's first answer to an unreachable branch. It is wider
            // than the three, and deliberately: anything else the load path
            // throws is still a file that did not become an assembly, and
            // that is better routed to the tool's owner with a message than
            // to exit 3 with a stack trace.
            throw Unreadable(exception);
        }

        return new LoadedPluginAssembly
        {
            Path = assemblyPath,
            AbstractionsReference = AbstractionsReferenceOf(assembly),
            Types = TypesOf(assembly),
        };
    }

    /// <remarks>
    /// Unloading every context, including ones opened before an assembly that
    /// failed. <see cref="AssemblyLoadContext.Unload"/> is a request rather
    /// than a guarantee — the runtime collects the context once nothing
    /// references anything inside it — so what this promises is that nothing on
    /// this side is still holding it open, which is the half a caller can be
    /// held to.
    /// </remarks>
    public void Dispose()
    {
        foreach (var context in _contexts)
        {
            context.Unload();
        }

        _contexts.Clear();
    }

    private static PluginAssemblyUnreadableException Unreadable(Exception exception) =>
        new(exception.Message);

    private static Version? AbstractionsReferenceOf(Assembly assembly) =>
        Array.Find(
            assembly.GetReferencedAssemblies(),
            reference => string.Equals(
                reference.Name,
                AbstractionsCompatibility.AssemblyName,
                StringComparison.Ordinal))?.Version;

    /// <summary>
    /// Every type the assembly declares, or a refusal naming what is missing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Assembly.GetTypes"/> rather than <c>GetExportedTypes</c>:
    /// <see cref="PluginLoader"/> has to see the non-public types too in order to
    /// notice one that names the rule interface from a foreign contract, and
    /// <see cref="RuleDiscovery"/> already declines an invisible type on its own
    /// terms.
    /// </para>
    /// <para>
    /// A missing dependency surfaces here rather than at load, because the CLR
    /// resolves references lazily: an assembly whose transitive dependency is
    /// absent opens without complaint and fails when its types are walked. It
    /// is never a partial result — the loader refuses to run the rules that
    /// happened to bind, since a plugin half loaded is the outcome the whole
    /// abort-on-failure rule exists to prevent.
    /// </para>
    /// <para>
    /// Excluded from coverage rather than tested into the green. Reaching
    /// the catch needs an assembly whose transitive dependency is absent from
    /// the machine, and every way to obtain one is worse than the branch:
    /// committing a deliberately broken binary puts an unreviewable file in the
    /// repository that nobody can regenerate, and emitting one at test time
    /// makes the suite depend on a persisted-assembly writer to test a message.
    /// What this code decides — that an unreadable assembly is exit 2, named,
    /// and never a partial load — is asserted through
    /// <see cref="IAssemblyLoader"/> in <c>PluginLoaderTests</c>, which is the
    /// substitution point that exists for it.
    /// </para>
    /// </remarks>
    [ExcludeFromCodeCoverage]
    private static Type[] TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            // The loader exceptions carry the names that are missing;
            // ReflectionTypeLoadException.Message is a summary that does not.
            // De-duplicated because one absent assembly produces one entry per
            // type that referenced it, which for a real plugin is every type.
            var reasons = exception.LoaderExceptions
                .OfType<Exception>()
                .Select(loaderException => loaderException.Message)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);

            throw new PluginAssemblyUnreadableException(
                $"its types could not be loaded, usually a missing dependency: {string.Join("; ", reasons)}");
        }
    }

    /// <summary>
    /// A collectible context that resolves the contract assembly to the host's.
    /// </summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string assemblyPath)
            : base($"preflight-plugin:{assemblyPath}", isCollectible: true) =>
            _resolver = new AssemblyDependencyResolver(assemblyPath);

        /// <remarks>
        /// <para>
        /// Returning <see langword="null"/> hands the name to the default
        /// context, which is the delegation the contract assembly requires. It
        /// is done for <c>Preflight.Abstractions</c> explicitly rather than
        /// left to the resolver, because a plugin that shipped its own copy —
        /// referenced without <c>Private=false</c>, which is the normal default
        /// and therefore the normal mistake — has one sitting right beside it,
        /// and the resolver would find it.
        /// </para>
        /// <para>
        /// Everything else resolves from the plugin's own dependency graph
        /// first, so two plugins may legitimately carry different versions of
        /// the same helper library. Anything the resolver does not know falls
        /// through to the default context, which is how the base class library
        /// arrives.
        /// </para>
        /// </remarks>
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            ArgumentNullException.ThrowIfNull(assemblyName);

            if (string.Equals(
                    assemblyName.Name,
                    AbstractionsCompatibility.AssemblyName,
                    StringComparison.Ordinal))
            {
                return null;
            }

            return ResolvePrivately(assemblyName);
        }

        /// <summary>
        /// A dependency shipped with the plugin, or nothing.
        /// </summary>
        /// <remarks>
        /// Excluded from coverage rather than tested, because no plugin in this
        /// repository has a private dependency and none should. The only plugin
        /// here is the worked example under <c>samples/</c>, whose entire point
        /// is that it references <c>Preflight.Abstractions</c> and nothing else
        /// — adding a package to it so that this branch could be reached would
        /// corrupt the thing a reader is meant to copy, in exchange for a
        /// percentage.
        ///
        /// What it decides is one line long and has no alternative reading: a
        /// path the resolver knows is loaded into this context, and a name it
        /// does not know falls through to the default one.
        /// </remarks>
        [ExcludeFromCodeCoverage]
        private Assembly? ResolvePrivately(AssemblyName assemblyName)
        {
            var path = _resolver.ResolveAssemblyToPath(assemblyName);

            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
