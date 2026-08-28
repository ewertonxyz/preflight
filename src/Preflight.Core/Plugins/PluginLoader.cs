namespace Preflight.Core.Plugins;

using Preflight.Abstractions;

/// <summary>
/// Turns the assemblies a run was pointed at into the rules it will execute.
/// </summary>
/// <remarks>
/// <para>
/// Every way this can go wrong is exit 2 and aborts the run; none of them is a
/// warning. A plugin quietly skipped means the run finished without rules the
/// policy declared enabled and reported success, which is the false green of
/// principle 7 — and it arrives with a second, misleading error, because the
/// policy keys of the skipped plugin then read as "unknown rule id" about a
/// rule the user wrote and can see on disk.
/// </para>
/// <para>
/// That is also why loading happens before policy validation, and why this type
/// is given the built-in rules rather than being asked only about plugins: the
/// set it returns is the whole rule universe, and a collision between a plugin
/// and a built-in is the same defect as a collision between two plugins. A
/// built-in rule and a plugin rule are the same kind of citizen; this is where
/// that stops being a slogan.
/// </para>
/// </remarks>
public sealed class PluginLoader
{
    private static readonly string RuleInterfaceFullName = typeof(IValidationRule).FullName!;

    private static readonly string HostContract = typeof(IValidationRule).Assembly.FullName!;

    private readonly IAssemblyLoader _loader;
    private readonly Version _hostAbstractions;

    public PluginLoader(IAssemblyLoader loader)
        : this(loader, AbstractionsCompatibility.HostVersion)
    {
    }

    /// <param name="loader">How an assembly is opened.</param>
    /// <param name="hostAbstractions">
    /// The contract version this engine provides. A parameter rather than a
    /// static read, so that the version refusal table can be exercised without
    /// shipping an engine per row.
    /// </param>
    public PluginLoader(IAssemblyLoader loader, Version hostAbstractions)
    {
        _loader = loader;
        _hostAbstractions = hostAbstractions;
    }

    /// <summary>
    /// The full rule set: the built-in rules plus everything the probed
    /// assemblies contributed.
    /// </summary>
    /// <exception cref="PluginLoadException">
    /// Anything at all went wrong. Accumulated, never stopped at the first.
    /// </exception>
    public IReadOnlyList<IValidationRule> Load(
        IReadOnlyList<IValidationRule> builtInRules,
        PluginProbeResult probe)
    {
        ArgumentNullException.ThrowIfNull(builtInRules);
        ArgumentNullException.ThrowIfNull(probe);

        var errors = new List<PluginLoadError>(probe.Errors);
        var pluginRules = new List<RuleOrigin>();

        foreach (var path in probe.AssemblyPaths)
        {
            LoadedPluginAssembly assembly;

            try
            {
                assembly = _loader.Load(path);
            }
            catch (PluginAssemblyUnreadableException exception)
            {
                errors.Add(new PluginLoadError.AssemblyUnreadable(path, exception.Message));

                continue;
            }

            Accept(assembly, errors, pluginRules);
        }

        var combined = Combine(builtInRules, pluginRules, errors);

        return errors.Count == 0
            ? combined
            : throw new PluginLoadException(errors);
    }

    /// <remarks>
    /// The order of the three checks is the order of their consequences. A
    /// contract version this engine cannot honour makes every judgement about
    /// the types inside meaningless, so it is decided first and the assembly is
    /// dropped. A foreign contract makes the types unusable individually, so
    /// the assembly contributes nothing rather than contributing the subset
    /// that happened to bind — a partial plugin is the outcome this is trying
    /// to prevent, not a salvage.
    /// </remarks>
    private void Accept(
        LoadedPluginAssembly assembly,
        List<PluginLoadError> errors,
        List<RuleOrigin> rules)
    {
        // Not a plugin. A rules directory legitimately holds helper assemblies a
        // rule depends on, and a run that refused to start because one of them
        // declares no rules would make the directory unusable for its purpose.
        if (assembly.AbstractionsReference is not { } reference)
        {
            return;
        }

        if (!AbstractionsCompatibility.IsCompatible(reference, _hostAbstractions))
        {
            errors.Add(new PluginLoadError.IncompatibleAbstractions(
                assembly.Path,
                reference,
                _hostAbstractions));

            return;
        }

        if (ReportForeignContracts(assembly, errors))
        {
            return;
        }

        try
        {
            rules.AddRange(
                RuleDiscovery.FromTypes(assembly.Types)
                    .Select(rule => new RuleOrigin(rule, assembly.Path)));
        }
        catch (RuleDiscoveryException exception)
        {
            // Re-wrapped rather than allowed to propagate. RuleDiscovery names
            // the type, which is enough when the types came from the engine's
            // own assembly and is not enough when four plugins were probed.
            errors.Add(new PluginLoadError.RuleTypeRejected(assembly.Path, exception.Message));
        }
    }

    /// <summary>
    /// Reports every type that names the rule interface without being one, and
    /// says whether it found any.
    /// </summary>
    /// <remarks>
    /// The detector for a bug that is easy to describe and easy to leave
    /// undetected. Delegating <c>Preflight.Abstractions</c> to the default load
    /// context is the fix, and a fix with no detector behind it fails the way
    /// this one does: an assembly that loads, produces no rules, and raises
    /// nothing. That is indistinguishable from an empty directory, which means
    /// the symptom of the defect is silence.
    /// </remarks>
    private static bool ReportForeignContracts(LoadedPluginAssembly assembly, List<PluginLoadError> errors)
    {
        var found = false;

        foreach (var type in assembly.Types)
        {
            if (ForeignRuleInterfaceOf(type) is not { } foreign)
            {
                continue;
            }

            errors.Add(new PluginLoadError.ForeignAbstractions(
                assembly.Path,

                // Null-forgiving rather than a fallback to Name: FullName is null
                // only for a generic parameter or an open array of one, and
                // Assembly.GetTypes never returns either. A '?? type.Name' beside
                // it would be a branch no assembly can reach.
                type.FullName!,
                foreign.Assembly.FullName!,
                HostContract));

            found = true;
        }

        return found;
    }

    /// <remarks>
    /// Matched by <see cref="Type.FullName"/> and not by identity, because
    /// identity is exactly the thing that is broken in the case being detected.
    /// The assignability test comes first so that a rule which really does
    /// implement this engine's contract never reaches the name comparison.
    /// </remarks>
    private static Type? ForeignRuleInterfaceOf(Type type) =>
        typeof(IValidationRule).IsAssignableFrom(type)
            ? null
            : Array.Find(
                type.GetInterfaces(),
                candidate => string.Equals(candidate.FullName, RuleInterfaceFullName, StringComparison.Ordinal));

    /// <remarks>
    /// <para>
    /// The built-in rules keep the order they arrived in and the plugin rules
    /// are sorted among themselves, because neither the graph nor any reporter
    /// reads this order — the graph orders by topological level with an ordinal
    /// tie-break, and <see cref="RuleDiscovery.FromTypes"/> has already sorted
    /// within each assembly. What matters here is only that the same inputs
    /// produce the same list, whatever order the directory was walked in.
    /// </para>
    /// <para>
    /// The fast path returns the built-in list itself. A run with no plugins is
    /// the overwhelmingly common one, and it should not pay for a copy, a sort
    /// or a grouping to arrive back where it started.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<IValidationRule> Combine(
        IReadOnlyList<IValidationRule> builtInRules,
        List<RuleOrigin> pluginRules,
        List<PluginLoadError> errors)
    {
        if (pluginRules.Count == 0)
        {
            return builtInRules;
        }

        var ordered = pluginRules
            .OrderBy(origin => origin.Rule.Descriptor.Id.Value, StringComparer.Ordinal)
            .ToArray();

        ReportCollisions(builtInRules, ordered, errors);

        return [.. builtInRules, .. ordered.Select(origin => origin.Rule)];
    }

    /// <remarks>
    /// A built-in rule is attributed to the assembly that declares its type and
    /// a plugin rule to the file it was loaded from, and the asymmetry is
    /// deliberate. A path is what the reader can act on, and it is the only
    /// identifier that separates the case that actually happens: the same
    /// plugin deployed into two directories, where a simple assembly name would
    /// report one claimant and hide the duplication entirely. A built-in has no
    /// path anyone chose, so its name is the most it can offer.
    /// </remarks>
    private static void ReportCollisions(
        IReadOnlyList<IValidationRule> builtInRules,
        IReadOnlyList<RuleOrigin> pluginRules,
        List<PluginLoadError> errors)
    {
        var claims = builtInRules
            .Select(rule => new RuleOrigin(rule, rule.GetType().Assembly.GetName().Name!))
            .Concat(pluginRules);

        foreach (var claim in claims.GroupBy(origin => origin.Rule.Descriptor.Id))
        {
            var assemblies = claim
                .Select(origin => origin.Assembly)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (assemblies.Length > 1)
            {
                errors.Add(new PluginLoadError.DuplicateRuleId(claim.Key, assemblies));
            }
        }
    }

    /// <summary>A rule and the assembly that is answerable for it.</summary>
    private readonly record struct RuleOrigin(IValidationRule Rule, string Assembly);
}
