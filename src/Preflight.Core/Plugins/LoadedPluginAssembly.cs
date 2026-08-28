namespace Preflight.Core.Plugins;

/// <summary>
/// One assembly a plugin path produced, reduced to what the loader judges it
/// on.
/// </summary>
/// <remarks>
/// <para>
/// The seam between "an assembly was opened" and "these rules exist".
/// Reflection over a real file is the one part of plugin loading that cannot be
/// exercised without a real file, so it is kept behind
/// <see cref="IAssemblyLoader"/> and reduced to this record — three facts, all of
/// which a test can state. Every decision the loader has to make is then a
/// decision about this record rather than about a DLL, and the whole version
/// refusal table becomes theory rows.
/// </para>
/// <para>
/// <see cref="Types"/> is every type the assembly declares, not the rules among
/// them. Filtering here would hide one of the most irritating bugs in .NET
/// plugin systems: a type that <em>says</em> it implements
/// <c>IValidationRule</c> and is not assignable to the one this engine knows. A
/// pre-filtered list cannot tell that apart from an assembly with no rules in
/// it.
/// </para>
/// </remarks>
public sealed record LoadedPluginAssembly
{
    /// <summary>
    /// Where it was loaded from, and the identity every message uses.
    /// </summary>
    /// <remarks>
    /// The path rather than the simple assembly name, because the same plugin
    /// deployed into two directories is two assemblies to the loader and one
    /// name to a reader — and a collision reported against one name would hide
    /// the duplication it exists to expose.
    /// </remarks>
    public required string Path { get; init; }

    /// <summary>
    /// The version of <c>Preflight.Abstractions</c> it was compiled against, or
    /// <see langword="null"/> when it does not reference it at all.
    /// </summary>
    /// <remarks>
    /// Null is not a failure. A plugin directory legitimately holds helper
    /// assemblies a rule depends on, and refusing a run because one of them is
    /// not itself a plugin would make the directory unusable for the thing it
    /// is for.
    /// </remarks>
    public required Version? AbstractionsReference { get; init; }

    /// <summary>Every type it declares.</summary>
    public required IReadOnlyList<Type> Types { get; init; }
}
