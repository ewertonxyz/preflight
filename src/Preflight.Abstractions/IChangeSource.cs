namespace Preflight.Abstractions;

/// <summary>
/// Produces the list of changed files for a run. Consumed by the engine, never
/// delivered to a rule.
/// </summary>
/// <remarks>
/// Exists as an interface because v1 implements git and a large production's
/// reality is Perforce. Not implementing Perforce is a scope decision; not
/// leaving the seam ready would have been an architecture one.
/// </remarks>
public interface IChangeSource
{
    string Name { get; }

    Task<IReadOnlyList<ChangedFile>> GetChangesAsync(
        DirectoryInfo workspaceRoot,
        string? fromRef,
        CancellationToken cancellationToken);
}
