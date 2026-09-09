namespace Preflight.Core.Execution;

using System.Collections;
using Preflight.Abstractions.Services;

/// <summary>
/// The environment probe the tool ships, reading the real process block.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than in the command line for the same reason the file system,
/// the process runner and the volume probe are: the integration layer of the
/// rule tests needs the shipped implementation, and a test project cannot
/// reference an executable.
/// </para>
/// <para>
/// The block is read once, when this is constructed, rather than on every call.
/// Rules at one level of the graph run concurrently, and a live read would let
/// two of them see different answers for the same name inside one run — a
/// report that contradicts itself, produced by nothing anybody did wrong.
/// </para>
/// <para>
/// Name lookup is the platform's own, which means it is case-insensitive on
/// Windows and case-sensitive everywhere else. Choosing one and applying it
/// everywhere would make the tool disagree with the shell that launched the
/// build: a workspace declaring <c>path</c> would be told it is set on a Linux
/// runner where nothing of that name exists.
/// </para>
/// </remarks>
public sealed class ProcessEnvironmentProbe : IEnvironmentProbe
{
    private readonly Dictionary<string, string?> _block;

    /// <summary>
    /// Takes the picture of the process environment this probe answers from.
    /// </summary>
    public ProcessEnvironmentProbe()
    {
        // The platform's own comparer, so that lookup here matches lookup in
        // the shell that started this process. It is the whole reason the block
        // is copied into a dictionary rather than kept as the hashtable the
        // runtime returns, which compares ordinally on every platform.
        _block = new Dictionary<string, string?>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            // A duplicate cannot arrive from the runtime, which already folds
            // the block into a dictionary of its own; the indexer is used
            // rather than Add so that a platform which one day disagrees keeps
            // the last value instead of throwing mid-construction.
            _block[(string)entry.Key] = entry.Value as string;
        }
    }

    /// <inheritdoc/>
    public string? Read(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _block.GetValueOrDefault(name);
    }
}
