namespace Preflight.Core.Plugins;

using Preflight.Abstractions;

/// <summary>
/// Which assemblies a run will probe, and which of the given paths were
/// unusable.
/// </summary>
/// <remarks>
/// The two travel together because a caller that took the paths and dropped the
/// errors would produce exactly the outcome this refuses: a run that finished
/// without the plugins somebody declared, and said nothing.
/// </remarks>
public sealed record PluginProbeResult(
    IReadOnlyList<string> AssemblyPaths,
    IReadOnlyList<PluginLoadError> Errors);

/// <summary>
/// Turns <c>--rules-path</c> and the implicit <c>rules/</c> directory into a
/// list of assemblies to open.
/// </summary>
/// <remarks>
/// <para>
/// Assemblies in <c>rules/</c> beside the executable, or in paths given by
/// <c>--rules-path</c>. Both sources, one resolution, one order.
/// </para>
/// <para>
/// Everything here goes through <see cref="IFileSystem"/>, which is what makes
/// the whole table of "given and missing", "given and a file", "implicit and
/// missing" a set of theory rows rather than a set of temporary directories.
/// </para>
/// </remarks>
public static class PluginPathResolution
{
    /// <summary>
    /// The directory probed beside the executable when no path is given.
    /// </summary>
    public const string ImplicitDirectoryName = "rules";

    /// <summary>The extension a plugin assembly has.</summary>
    private const string AssemblyPattern = "*.dll";

    /// <summary>
    /// Resolves every source into the assemblies to open.
    /// </summary>
    /// <param name="fileSystem">How the directories are inspected.</param>
    /// <param name="workspaceRoot">What a relative <c>--rules-path</c> is relative to.</param>
    /// <param name="executableDirectory">
    /// Where the implicit <see cref="ImplicitDirectoryName"/> directory is
    /// looked for.
    /// </param>
    /// <param name="requestedPaths">Every <c>--rules-path</c>, in the order given.</param>
    /// <remarks>
    /// <para>
    /// A relative path resolves against the workspace root, which is the
    /// directory the tool was invoked in. The alternative — resolving against
    /// the executable — would make <c>--rules-path ./rules</c> mean something
    /// different from what the shell's own tab completion just showed the user.
    /// </para>
    /// <para>
    /// The implicit directory resolves against the executable and never against
    /// the workspace, and that is a security property rather than a
    /// convenience. A workspace is frequently a checkout the person running
    /// <c>preflight</c> did not write; resolving <c>rules/</c> against it would
    /// execute code committed to the repository being validated, on the first
    /// run, with no flag and no prompt.
    /// </para>
    /// <para>
    /// The result is sorted and de-duplicated by full path. Sorting is what
    /// makes a collision message stable — load order must not decide anything,
    /// and enumeration order is exactly what a file system does not promise.
    /// De-duplication is what stops <c>--rules-path ./rules</c>, pointing at
    /// the directory that would also have been probed implicitly, from
    /// colliding with itself.
    /// </para>
    /// </remarks>
    public static PluginProbeResult Resolve(
        IFileSystem fileSystem,
        DirectoryInfo workspaceRoot,
        DirectoryInfo executableDirectory,
        IReadOnlyList<string> requestedPaths)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(executableDirectory);
        ArgumentNullException.ThrowIfNull(requestedPaths);

        var errors = new List<PluginLoadError>();
        var directories = new List<string>();

        foreach (var requested in requestedPaths)
        {
            var resolved = Path.GetFullPath(requested, workspaceRoot.FullName);

            if (fileSystem.DirectoryExists(resolved))
            {
                directories.Add(resolved);

                continue;
            }

            errors.Add(new PluginLoadError.PluginPathUnusable(
                requested,
                fileSystem.FileExists(resolved)
                    ? "is a file; it must be a directory holding plugin assemblies"
                    : "does not exist"));
        }

        var implicitDirectory = Path.Combine(executableDirectory.FullName, ImplicitDirectoryName);

        if (fileSystem.DirectoryExists(implicitDirectory))
        {
            directories.Add(Path.GetFullPath(implicitDirectory));
        }

        return new PluginProbeResult(AssembliesIn(fileSystem, directories), errors);
    }

    /// <remarks>
    /// <see cref="SearchOption.TopDirectoryOnly"/>, deliberately. A recursive
    /// probe turns <c>--rules-path .</c> into an attempt to load every assembly
    /// under a checkout — every <c>bin/</c>, every test binary, every restored
    /// package staged on disk — and the first of them that fails takes the run
    /// down with it.
    /// </remarks>
    private static IReadOnlyList<string> AssembliesIn(
        IFileSystem fileSystem,
        IReadOnlyList<string> directories) =>
        [.. directories
            .SelectMany(directory =>
                fileSystem.EnumerateFiles(directory, AssemblyPattern, SearchOption.TopDirectoryOnly))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)];
}
