namespace Preflight.TestSupport;

/// <summary>
/// Locates paths inside the repository from a test that is running out of its
/// own output folder.
/// </summary>
/// <remarks>
/// Every test that needs a file from the repository — an assembly reference to
/// inspect, a workspace fixture to validate against — has to answer this
/// question, and there is exactly one correct way to answer it.
///
/// The tempting alternative is a path relative to the current directory. It
/// passes: scripts/coverage.ps1 sets the working directory to the repository
/// root before it runs anything. It then fails under an IDE runner, whose
/// working directory is the project's bin folder. A test that passes in one
/// runner and fails in another reads as flakiness, and flakiness is what gets a
/// test deleted rather than fixed.
///
/// Counting directory levels up from the output folder is the other tempting
/// answer, and it breaks the moment the configuration, the target framework or
/// an artifacts path changes — again, a failure with nothing to do with what
/// the test asserts. Walking up until the solution file appears is stable
/// against all three.
/// </remarks>
public static class RepositoryLayout
{
    private const string SolutionFileName = "Preflight.slnx";

    /// <summary>
    /// The absolute path of the repository root — the directory holding
    /// <c>Preflight.slnx</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The solution file was not found above the test's output folder.
    /// </exception>
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException(
                $"Could not locate {SolutionFileName} above {AppContext.BaseDirectory}.")
            : directory.FullName;
    }

    /// <summary>
    /// Combines <paramref name="segments"/> onto the repository root.
    /// </summary>
    public static string PathFromRoot(params string[] segments) =>
        Path.Combine([RepositoryRoot(), .. segments]);

    /// <summary>
    /// The build output of another project in this repository, for the same
    /// configuration and framework the caller was built for.
    /// </summary>
    /// <param name="projectDirectory">
    /// Its directory relative to the repository root, for example
    /// <c>samples/Sample.Production.Rules</c>.
    /// </param>
    /// <param name="fileName">The file wanted inside that output folder.</param>
    /// <remarks>
    /// For the one case <see cref="PathFromRoot"/> cannot serve: a test that
    /// needs another project's <em>artefact</em> rather than its source. The
    /// configuration and the framework are read off the caller's own output
    /// path, which is the only place they are authoritative at run time —
    /// hard-coding <c>Release</c> would pass in CI and fail in an IDE, and
    /// hard-coding <c>Debug</c> would do the reverse.
    ///
    /// This is not the level-counting <see cref="RepositoryRoot"/> warns
    /// against: nothing here counts how far up the root is, and the two
    /// segments taken are the two the SDK writes into every output path.
    /// </remarks>
    public static string BuildOutputPathOf(string projectDirectory, string fileName)
    {
        ArgumentNullException.ThrowIfNull(projectDirectory);

        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var framework = output.Name;
        var configuration = output.Parent!.Name;

        return Path.Combine(
            RepositoryRoot(),
            Path.Combine(projectDirectory.Split('/')),
            "bin",
            configuration,
            framework,
            fileName);
    }
}
