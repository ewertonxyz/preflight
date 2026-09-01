namespace Preflight.Cli.Commands;

using Preflight.Abstractions.Rules;
using Preflight.Core.Plugins;

/// <summary>
/// The one place a command's rule set is decided.
/// </summary>
/// <remarks>
/// <para>
/// Plugin loading happens before policy validation, because the other order
/// produces a second, misleading error. This sits at the single dispatch point
/// in
/// <see cref="PreflightCommandLine.Run"/> so that the ordering holds for every
/// command rather than for the one somebody remembered.
/// </para>
/// <para>
/// That every command sees plugins is a decision, not a side effect. Six of the
/// seven resolve a policy, and a policy naming a plugin's rule is rejected with
/// "unknown rule id" by any command that cannot see the plugin — which is
/// precisely that misleading second error, arrived at through a different door.
/// <c>graph</c> resolves no policy and is included anyway: a graph that omits
/// half the rules is not a diffable picture of the run, which is the whole of
/// what it exists to be.
/// </para>
/// </remarks>
public static class PluginLoading
{
    /// <summary>
    /// The built-in rules plus everything the plugin paths contributed.
    /// </summary>
    /// <param name="environment">Where the workspace, the executable and the file system are.</param>
    /// <param name="loader">The open load contexts, owned by the caller.</param>
    /// <param name="requestedPaths">Every <c>--rules-path</c>, in the order given.</param>
    /// <exception cref="PluginLoadException">
    /// A path, an assembly or a rule id was unusable. Exit 2.
    /// </exception>
    public static IReadOnlyList<IValidationRule> Compose(
        CommandEnvironment environment,
        IAssemblyLoader loader,
        IReadOnlyList<string> requestedPaths)
    {
        ArgumentNullException.ThrowIfNull(environment);

        // The resolved package's own rules, added as one more probe path rather
        // than through a second discovery mechanism. A second path would be a
        // second load order, and an id colliding across the two would be
        // resolved by whichever ran first — which is the rule ADR-025 refuses.
        // Coming in here, a collision between a package rule and one from
        // --rules-path is the ordinary collision, reported with both assemblies
        // named and nobody winning.
        //
        // Only the resolved version's directory. Other installed versions of the
        // same pipeline are invisible, or every second install would be an id
        // collision and the feature would be unusable.
        // Built by hand rather than with a spread into a collection expression.
        // The spread compiles to a length probe on the source, which is a branch
        // this file did not write and no test can steer — and one unreachable
        // branch is the difference between "everything here is exercised" and
        // "almost".
        var paths = new List<string>(requestedPaths);

        if (RulesDirectoryOf(environment) is { } packageRules)
        {
            paths.Add(packageRules);
        }

        var probe = PluginPathResolution.Resolve(
            environment.FileSystem,
            environment.WorkspaceRoot,
            environment.ExecutableDirectory,
            paths);

        return new PluginLoader(loader).Load(environment.Rules, probe);
    }

    /// <summary>
    /// The resolved package's <c>rules/</c> directory, or <see langword="null"/>
    /// when there is no package or it carries none.
    /// </summary>
    /// <remarks>
    /// "Carries none" has to collapse into the same answer as "no package": a
    /// path that does not exist is reported by
    /// <see cref="PluginPathResolution"/> as an unusable <c>--rules-path</c>,
    /// and a policy-only package — the common case — would then refuse every
    /// command with a complaint about a directory its author never claimed to
    /// ship.
    /// </remarks>
    private static string? RulesDirectoryOf(CommandEnvironment environment)
    {
        if (environment.ResolvedPackage is not { } package)
        {
            return null;
        }

        var directory = Path.Combine(package.Root.FullName, PluginPathResolution.ImplicitDirectoryName);

        // Through the seam, like every other directory question this file asks.
        // A direct File.Exists here would be the one read that a substituted
        // file system could not answer for.
        return environment.FileSystem.DirectoryExists(directory) ? directory : null;
    }
}
