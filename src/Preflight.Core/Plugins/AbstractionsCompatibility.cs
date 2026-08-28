namespace Preflight.Core.Plugins;

using System.Globalization;
using Preflight.Abstractions;

/// <summary>
/// The plugin version check, as a function.
/// </summary>
/// <remarks>
/// <para>
/// Pure, and separate from anything that opens a file, because the table it
/// implements is the part of plugin loading with a right answer written down.
/// Every row of 11.2 is an input pair here, which is what makes the table
/// testable without producing one assembly per row.
/// </para>
/// <para>
/// The comparison takes the whole version — major, minor and patch — rather
/// than a truncated pair, so that a refusal can name exactly what the plugin
/// asked for. What <em>decides</em> is narrower than what is reported, and
/// deliberately so: 11.2 defines patch as documentation only, so a plugin built
/// against 1.2.5 running on a 1.2.0 host loads. Refusing it would reject a run
/// over a difference the versioning policy itself says means nothing.
/// </para>
/// </remarks>
public static class AbstractionsCompatibility
{
    /// <summary>The name every plugin references and no plugin carries.</summary>
    public const string AssemblyName = "Preflight.Abstractions";

    /// <summary>
    /// The version of <c>Preflight.Abstractions</c> this engine provides.
    /// </summary>
    /// <remarks>
    /// Read once from the loaded assembly. Null-forgiving rather than a
    /// fallback, for the reason
    /// <see cref="Caching.CachePaths.AbstractionsMajor"/> gives:
    /// <c>AssemblyName.Version</c> is annotated nullable and is never null for
    /// an assembly the runtime has loaded, and this expression begins by taking
    /// a type out of it.
    /// </remarks>
    public static Version HostVersion { get; } =
        typeof(IValidationRule).Assembly.GetName().Version!;

    /// <summary>
    /// Whether a plugin built against <paramref name="plugin"/> may run on a
    /// host providing <paramref name="host"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two conditions, and each is one row of 11.2. A different major is a
    /// different contract in both directions — an older plugin asks for surface
    /// that was removed, a newer one asks for surface that does not exist yet.
    /// A minor above the host's is the asymmetric case the table spends its
    /// third row on: the host's surface is a superset of an older minor, so
    /// 1.2.0 on 1.4.0 loads, while 1.4.0 on 1.2.0 asks for members nobody
    /// compiled.
    /// </para>
    /// <para>
    /// A pre-1.0 plugin needs no arm of its own. Under SemVer, 0.x is a
    /// different major from 1.x, and the first condition already refuses it —
    /// with the same message, which names both versions and is therefore not
    /// misleading for that case. An arm written specially for it would be a
    /// branch reachable only by a plugin nobody can build against a released
    /// contract.
    /// </para>
    /// </remarks>
    public static bool IsCompatible(Version plugin, Version host)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(host);

        return plugin.Major == host.Major && plugin.Minor <= host.Minor;
    }

    /// <summary>
    /// The refusal message: both versions, the plugin, and what would fix it.
    /// </summary>
    /// <remarks>
    /// One message for every refused row rather than one per reason. A reader
    /// holding two version numbers and the file they disagree about can tell
    /// which side is behind without being told, and a switch naming each row
    /// would put arms in the branch count that say nothing the numbers do not.
    /// </remarks>
    public static string RefusalFor(string assemblyPath, Version plugin, Version host) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Plugin '{0}' was built against {1} {2} and this engine provides {3}. " +
            "Rebuild the plugin against {1} {3}, or upgrade preflight.",
            assemblyPath,
            AssemblyName,
            plugin,
            host);
}
