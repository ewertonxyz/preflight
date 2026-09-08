namespace Preflight.Rules;

using Preflight.Abstractions.Services;

/// <summary>
/// Finds two paths in one directory that differ only in case.
/// </summary>
/// <remarks>
/// <para>
/// The pair is created without complaint on a case-sensitive filesystem and
/// collapses into one file when the repository is checked out on Windows or
/// macOS. Whichever file loses, the build that needed it fails on a machine
/// that never touched the commit.
/// </para>
/// <para>
/// Compared against the change set <em>and</em> against the files already
/// sitting in each changed file's own directory, one listing per distinct
/// parent and never recursive. Comparing the change set only against itself
/// would miss the case that actually happens — a new file colliding with one
/// that has been in the repository for years — and walking the whole repository
/// is the cost a pre-submit check exists in order not to pay.
/// </para>
/// <para>
/// Files only. There is no way to list directories through the contract a rule
/// is given, and adding one would be a member on the interface every plugin
/// implements — the most expensive change this project can make, to catch the
/// rarer half of the problem. The finding says "file" so that the reader is not
/// left wondering why the directory pair went unreported.
/// </para>
/// </remarks>
internal sealed class CaseCollisionCheck : IPathPortabilityCheck
{
    /// <summary>
    /// The name the policy selects this check by.
    /// </summary>
    public const string CheckName = "case-collision";

    private readonly IFileSystem _fileSystem;

    private readonly DirectoryInfo _workspaceRoot;

    /// <summary>
    /// Creates the check over the workspace it will read.
    /// </summary>
    /// <param name="fileSystem">Read-only access to the workspace.</param>
    /// <param name="workspaceRoot">The directory being validated.</param>
    /// <remarks>
    /// The only check with collaborators, which is why it takes them here
    /// rather than in <see cref="Inspect"/>: the other three answer from the
    /// paths alone, and widening the shared call for the sake of one
    /// implementation would hand every check a file system it must be trusted
    /// not to use.
    /// </remarks>
    public CaseCollisionCheck(IFileSystem fileSystem, DirectoryInfo workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(workspaceRoot);

        _fileSystem = fileSystem;
        _workspaceRoot = workspaceRoot;
    }

    /// <inheritdoc/>
    public string Name => CheckName;

    /// <inheritdoc/>
    public IReadOnlyList<PathDefect> Inspect(IReadOnlyList<string> paths, PathPortabilityLimits limits)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var defects = new List<PathDefect>();
        var siblings = SiblingsByDirectory(paths);
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            // Against the rest of the change set first. Two files arriving in
            // one commit that differ only in case is the case a listing cannot
            // see, because neither of them is on disk yet.
            if (seen.TryGetValue(path, out var earlier))
            {
                defects.Add(Collision(path, earlier, "another path in this change"));
            }
            else
            {
                seen[path] = path;
            }

            if (Match(siblings, path) is { } onDisk)
            {
                defects.Add(Collision(path, onDisk, "a file already in the workspace"));
            }
        }

        return defects;
    }

    /// <summary>
    /// The files sitting in each directory the change set touches.
    /// </summary>
    /// <remarks>
    /// One listing per distinct parent, deduplicated before the walk and never
    /// recursive, so the cost is bounded by the size of the change rather than
    /// by the size of the repository. A directory that is not there yet is
    /// skipped rather than listed: a file added into a new directory is the
    /// ordinary case, and asking for a listing would throw out of the rule.
    /// </remarks>
    private Dictionary<string, List<string>> SiblingsByDirectory(IReadOnlyList<string> paths)
    {
        var listings = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var directory in paths.Select(DirectoryOf).Distinct(StringComparer.Ordinal))
        {
            var absolute = directory.Length == 0
                ? _workspaceRoot.FullName
                : Path.Combine(_workspaceRoot.FullName, directory);

            if (!_fileSystem.DirectoryExists(absolute))
            {
                continue;
            }

            listings[directory] =
            [
                .. _fileSystem.EnumerateFiles(absolute, "*", SearchOption.TopDirectoryOnly)
                    .Select(entry => Relative(entry)),
            ];
        }

        return listings;
    }

    /// <remarks>
    /// A sibling equal to the path when compared without regard to case, but
    /// not equal to it exactly. Without the second half, every modified file
    /// would collide with itself and the rule would fail every clean commit.
    /// </remarks>
    private static string? Match(Dictionary<string, List<string>> siblings, string path) =>
        siblings.TryGetValue(DirectoryOf(path), out var listing)
            ? listing.Find(entry =>
                string.Equals(entry, path, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(entry, path, StringComparison.Ordinal))
            : null;

    private string Relative(string absolute) =>
        Path.GetRelativePath(_workspaceRoot.FullName, absolute).Replace('\\', '/');

    private static string DirectoryOf(string path) =>
        path.LastIndexOf('/') is var slash && slash >= 0 ? path[..slash] : string.Empty;

    private static PathDefect Collision(string path, string other, string where) => new(
        path,
        $"differs only in case from {where}",
        $"one file per name, whatever the case: '{path}' and '{other}' cannot both exist");
}
