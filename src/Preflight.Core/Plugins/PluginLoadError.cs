namespace Preflight.Core.Plugins;

using System.Globalization;
using Preflight.Abstractions.Rules;

/// <summary>
/// One reason a plugin directory could not be turned into a set of rules.
/// </summary>
/// <remarks>
/// <para>
/// The ways loading can fail, every one of them exit 2 rather than a warning.
/// Each carries its evidence structurally — the two assemblies that claimed an
/// id, the two contract identities behind a type mismatch — for the reason
/// <see cref="GraphValidationError"/> gives: a reporter should never have to
/// parse prose back out of a message.
/// </para>
/// <para>
/// A closed hierarchy with <see cref="Message"/> overridden per case, and no
/// switch anywhere over the cases. A switch would need a discard arm it cannot
/// prove unreachable, which is a permanent hole in the branch count for
/// nothing: the message belongs to the case, so the case writes it.
/// </para>
/// </remarks>
public abstract record PluginLoadError
{
    private PluginLoadError()
    {
    }

    /// <summary>What the user is told.</summary>
    public abstract string Message { get; }

    /// <summary>
    /// A path given by <c>--rules-path</c> that is not a directory to probe.
    /// </summary>
    /// <remarks>
    /// Only ever raised for a path the user named. The implicit <c>rules/</c>
    /// directory beside the executable is absent on every installation that has
    /// no plugins, and refusing to run because of it would make an empty
    /// deployment invalid. What separates the two is that the user asked for
    /// this one, so silently ignoring it would finish a run without the rules
    /// they declared.
    /// </remarks>
    public sealed record PluginPathUnusable(string Path, string Reason) : PluginLoadError
    {
        public override string Message =>
            $"--rules-path '{Path}' {Reason}.";
    }

    /// <summary>
    /// A file that would not open as an assembly, or whose dependencies would
    /// not resolve.
    /// </summary>
    public sealed record AssemblyUnreadable(string Path, string Reason) : PluginLoadError
    {
        public override string Message =>
            $"Plugin assembly '{Path}' could not be loaded: {Reason}";
    }

    /// <summary>
    /// A plugin built against a contract this engine does not provide.
    /// </summary>
    public sealed record IncompatibleAbstractions(string Path, Version Plugin, Version Host) : PluginLoadError
    {
        public override string Message =>
            AbstractionsCompatibility.RefusalFor(Path, Plugin, Host);
    }

    /// <summary>
    /// A type that implements an <c>IValidationRule</c> from a different
    /// <c>Preflight.Abstractions</c> than the engine's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One of the most irritating bugs in .NET plugin systems, and the reason
    /// the load context delegates the contract assembly to the default one.
    /// Without this case the symptom is an assembly that loads, contributes
    /// nothing, and says nothing — indistinguishable from an empty directory,
    /// and a green run missing the rules a production declared.
    /// </para>
    /// <para>
    /// Both identities are named because neither alone is actionable. The usual
    /// cause is a <c>Preflight.Abstractions.dll</c> shipped alongside the
    /// plugin, which is what <c>Private=false</c> in 11.1 exists to prevent,
    /// and the person reading the message needs to see two of them to believe
    /// it.
    /// </para>
    /// </remarks>
    public sealed record ForeignAbstractions(
        string Path,
        string TypeName,
        string PluginContract,
        string HostContract) : PluginLoadError
    {
        public override string Message =>
            $"Type '{TypeName}' in plugin assembly '{Path}' implements " +
            $"'{AbstractionsCompatibility.AssemblyName}.{nameof(IValidationRule)}' from '{PluginContract}', " +
            $"but this engine's contract is '{HostContract}'. The two are different types to the runtime, so " +
            "the rule would be silently ignored. Reference " +
            $"{AbstractionsCompatibility.AssemblyName} with Private=false so the plugin does not ship its own copy.";
    }

    /// <summary>
    /// A type that declared itself a rule and could not be turned into one.
    /// </summary>
    /// <remarks>
    /// Carries the assembly as well as the reason. <see cref="RuleDiscovery"/>
    /// names the type, which is enough when the types came from the engine's
    /// own assembly and is not enough when a directory held four plugins.
    /// </remarks>
    public sealed record RuleTypeRejected(string Path, string Reason) : PluginLoadError
    {
        public override string Message =>
            $"Plugin assembly '{Path}': {Reason}";
    }

    /// <summary>
    /// One rule id claimed by more than one assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Choosing one of the two by load order would make the result depend on
    /// the file system's enumeration order, which is the definition of
    /// non-deterministic. The assemblies are therefore listed in ordinal order
    /// rather than in the order they were opened, so the same two plugins
    /// produce the same message however the directory was walked or the flags
    /// were typed.
    /// </para>
    /// <para>
    /// Between assemblies only. The same id declared twice <em>inside</em> one
    /// assembly is still <see cref="GraphValidationError.DuplicateRuleId"/>'s
    /// to report: that case has no second assembly to name, and the graph
    /// already refuses it.
    /// </para>
    /// </remarks>
    public sealed record DuplicateRuleId(RuleId RuleId, IReadOnlyList<string> Assemblies) : PluginLoadError
    {
        public override string Message =>
            string.Format(
                CultureInfo.InvariantCulture,
                "Rule id '{0}' is declared by more than one assembly: {1}. " +
                "Rename one of them; picking by load order would make the run depend on directory enumeration.",
                RuleId.Value,
                string.Join(", ", Assemblies.Select(name => $"'{name}'")));
    }
}
