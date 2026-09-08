namespace Preflight.Rules;

/// <summary>
/// Finds a path, or one component of it, longer than the policy accepts.
/// </summary>
/// <remarks>
/// Measured against the path relative to the workspace root, never the absolute
/// one. The absolute path is the number that actually breaks on disk, and it is
/// also a number that differs between two machines checking out the same
/// commit — so a rule measuring it would give two verdicts for one repository,
/// which is the one thing this tool must never do. The relative length is the
/// part the repository controls, and the limit is policy so that a production
/// whose build agents check out deep can raise it in one line.
/// </remarks>
internal sealed class PathLengthCheck : IPathPortabilityCheck
{
    /// <summary>
    /// The name the policy selects this check by.
    /// </summary>
    public const string CheckName = "path-length";

    /// <inheritdoc/>
    public string Name => CheckName;

    /// <inheritdoc/>
    public IReadOnlyList<PathDefect> Inspect(IReadOnlyList<string> paths, PathPortabilityLimits limits)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(limits);

        var defects = new List<PathDefect>();

        foreach (var path in paths)
        {
            // The whole path first, then each component, and both are reported
            // when both are over: they are two problems with two different
            // remedies — move the file, or rename the part that is too long.
            if (path.Length > limits.MaxPathLength)
            {
                defects.Add(new PathDefect(
                    path,
                    $"is {path.Length} characters long",
                    $"at most {limits.MaxPathLength} characters in the path relative to the workspace root"));
            }

            foreach (var component in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (component.Length > limits.MaxComponentLength)
                {
                    defects.Add(new PathDefect(
                        path,
                        $"has a component {component.Length} characters long",
                        $"at most {limits.MaxComponentLength} characters in any one name"));
                }
            }
        }

        return defects;
    }
}
