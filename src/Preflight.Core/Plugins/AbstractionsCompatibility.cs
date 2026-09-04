namespace Preflight.Core.Plugins;

using System.Globalization;
using Preflight.Abstractions.Rules;

/// <summary>
/// The plugin version check, as a function.
/// </summary>
/// <remarks>
/// <para>
/// Pure, and separate from anything that opens a file, because the refusal
/// table is the part of plugin loading with a right answer. Each row of that
/// table is an input pair here, which is what makes it testable without
/// producing one assembly per row — the alternative is a project per row, each
/// a copy of the same source under a different version number, testing the
/// build system rather than the rule.
/// </para>
/// <para>
/// The comparison takes the whole version — major, minor and patch — rather
/// than a truncated pair, so that a refusal can name exactly what the plugin
/// asked for. What <em>decides</em> is narrower than what is reported, and
/// deliberately so: patch is documentation, so a plugin built against 1.2.5
/// running on a 1.2.0 host loads. Refusing it would reject a run over a
/// difference the versioning policy itself says means nothing.
/// </para>
/// </remarks>
public static class AbstractionsCompatibility
{
    /// <summary>The name every plugin references and no plugin carries.</summary>
    public const string AssemblyName = "Preflight.Abstractions";

    /// <summary>
    /// The version of <c>Preflight.Abstractions</c> this tool provides.
    /// </summary>
    /// <remarks>
    /// Read once from the loaded assembly, so a defect here is a refusal table
    /// computed against the wrong number for the life of the process rather
    /// than for one call. Null-forgiving rather than a fallback:
    /// <c>AssemblyName.Version</c> is annotated nullable and is never null for
    /// an assembly the runtime has loaded, and this expression begins by taking
    /// a type out of it, so a <c>?? …</c> beside it would be a branch no input
    /// can reach.
    /// </remarks>
    public static Version HostVersion { get; } =
        typeof(IValidationRule).Assembly.GetName().Version!;

    /// <summary>
    /// What identifies one unbroken line of the contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two versions of the same generation promise each other source and binary
    /// compatibility in one direction; two of different generations promise
    /// nothing. Above 1.0 that is the major, and <c>1.4.0</c> answers <c>1</c>.
    /// Below it the leading zero shifts the whole scheme one place right —
    /// SemVer says a 0.x minor may break anything — so <c>0.1.1</c> answers
    /// <c>0.1</c> and <c>0.2.0</c> answers <c>0.2</c>.
    /// </para>
    /// <para>
    /// It exists as a named function because two callers need the rule and
    /// neither should carry a second copy of it: the loader decides whether a
    /// plugin may run, and the cache key decides whether a stored result is
    /// still readable. Those two answering differently is a cached pass served
    /// under a contract that changed, which is the most expensive defect this
    /// tool can produce, because the evidence of it is the run that did not
    /// happen.
    /// </para>
    /// </remarks>
    public static string GenerationOf(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return version.Major == 0
            ? string.Create(CultureInfo.InvariantCulture, $"0.{version.Minor}")
            : version.Major.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether a plugin built against <paramref name="plugin"/> may run on a
    /// host providing <paramref name="host"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two conditions. A different generation is a different contract in both
    /// directions — an older plugin asks for surface that was removed, a newer
    /// one asks for surface that does not exist yet. A minor above the host's
    /// is the asymmetric case: the host's surface is a superset of an older
    /// minor, so 1.2.0 on 1.4.0 loads, while 1.4.0 on 1.2.0 asks for members
    /// nobody compiled.
    /// </para>
    /// <para>
    /// On a 0.x line the second condition decides nothing, because equal
    /// generations already mean equal minors there — 0.1.0 on 0.2.0 is refused
    /// by the first. That is the point of routing both through
    /// <see cref="GenerationOf"/> rather than comparing majors directly: a
    /// plain <c>plugin.Major == host.Major</c> would let a plugin built against
    /// 0.1 load on 0.2, which is precisely the release SemVer allows to break
    /// it, and the failure would surface as a type load somewhere far from the
    /// cause.
    /// </para>
    /// <para>
    /// Patch never decides, on either line. A plugin built against 1.2.5
    /// running on a 1.2.0 host loads, because refusing it would reject a run
    /// over a difference the versioning policy itself defines as documentation.
    /// </para>
    /// </remarks>
    public static bool IsCompatible(Version plugin, Version host)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(host);

        return string.Equals(GenerationOf(plugin), GenerationOf(host), StringComparison.Ordinal)
            && plugin.Minor <= host.Minor;
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
            "Plugin '{0}' was built against {1} {2} and this tool provides {3}. " +
            "Rebuild the plugin against {1} {3}, or upgrade preflight.",
            assemblyPath,
            AssemblyName,
            plugin,
            host);
}
