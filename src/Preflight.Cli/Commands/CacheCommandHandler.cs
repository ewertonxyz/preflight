namespace Preflight.Cli.Commands;

using Preflight.Cli.Model;
using Preflight.Cli.Policy;
using Preflight.Core.Caching;
using Preflight.Core.History;

/// <summary>
/// <c>preflight cache clear</c>.
/// </summary>
/// <remarks>
/// It resolves the policy chain for one value, <c>cachePath</c>, and for the
/// same reason <c>measure</c> and <c>report</c> do: one rule about when the CLI
/// accepts broken configuration, not one per command. Clearing a cache whose
/// location came from a policy nobody could parse would be emptying a directory
/// chosen by guesswork.
/// </remarks>
public static class CacheCommandHandler
{
    public static async Task<int> ClearAsync(
        CommandEnvironment environment,
        RunOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);

        var resolved = await PolicyResolution.ResolveAsync(
            environment.WorkspaceRoot,
            environment.FileSystem,
            environment.Environment,
            [.. environment.Rules.Select(rule => rule.Descriptor)],
            options,
            environment.Selection,
            cancellationToken,
            environment.ResolvedPackage);

        var directory = CachePaths.DirectoryFor(
            environment.WorkspaceRoot,
            CacheSettings.From(resolved.Policy).Path);

        // Before anything is deleted. 'cachePath' is a free string that any
        // policy overlay may set: "." would turn this command into one that
        // empties the repository, and ".preflight" would take the run history
        // with it. Exit 2 through the CLI's existing catch.
        RuleCache.RequireSafeToEmpty(
            environment.WorkspaceRoot,
            directory,
            HistoryPaths.DirectoryFor(environment.WorkspaceRoot, HistorySettings.From(resolved.Policy)));

        var removed = environment.Cache.Clear(directory);

        // The count, not a bare "done". A cache that was already empty and a
        // cache that just lost four hundred entries are different facts, and the
        // second one is worth seeing before a fifteen-second probe re-runs.
        var count = removed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var noun = removed == 1 ? "cached result" : "cached results";

        environment.Console.Output.Write($"Removed {count} {noun} from {directory}\n");

        return ExitCode.Success;
    }
}
