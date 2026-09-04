namespace Preflight.Abstractions.Services;

using Preflight.Abstractions.Model;

/// <summary>
/// Produces the list of changed files for a run. Consumed by the tool, never
/// delivered to a rule.
/// </summary>
/// <remarks>
/// Exists as an interface because v1 implements git and a large production's
/// reality is Perforce. Not implementing Perforce is a scope decision; not
/// leaving the substitution point ready would have been an architecture one.
/// </remarks>
public interface IChangeSource
{
    /// <summary>
    /// The version control system this reads — <c>git</c> for the one
    /// implementation that ships.
    /// </summary>
    /// <remarks>
    /// Nothing reads it yet, and that is worth saying so the next reader does
    /// not go looking for the consumer. It is here because a run that produced
    /// its changed files from Perforce and one that produced them from git are
    /// not interchangeable in a report, and the report cannot say which it was
    /// unless the source can name itself. Removing it and adding it back when
    /// the second implementation arrives would cost a major version of this
    /// assembly and a recompile of every plugin, which is more than an unread
    /// string is worth.
    /// </remarks>
    string Name { get; }

    Task<IReadOnlyList<ChangedFile>> GetChangesAsync(
        DirectoryInfo workspaceRoot,
        string? fromRef,
        CancellationToken cancellationToken);
}
