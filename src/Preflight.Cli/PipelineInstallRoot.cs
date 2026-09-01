namespace Preflight.Cli;

using Preflight.Core.Policy;

/// <summary>
/// Where installed pipeline packages live on this machine.
/// </summary>
/// <remarks>
/// <para>
/// A machine fact, not a repository one. Which pipeline a checkout is stays in
/// the checkout, where ADR-029 put it; which version of it is on this disk
/// cannot live there, because one developer holds the clones of two games on one
/// machine and only one of those checkouts could be right about what is
/// installed.
/// </para>
/// <para>
/// The layout is <c>&lt;root&gt;/pipelines/&lt;name&gt;/&lt;version&gt;/</c>, with
/// the machine's own state beside it. See ADR-032.
/// </para>
/// </remarks>
/// <param name="Root">The resolved root directory.</param>
public sealed record PipelineInstallRoot(DirectoryInfo Root)
{
    /// <summary>The variable that overrides the default location.</summary>
    public const string HomeVariable = "PREFLIGHT_HOME";

    /// <summary>The variable the default location is built from.</summary>
    public const string LocalAppDataVariable = "LOCALAPPDATA";

    /// <summary>The directory name appended to <see cref="LocalAppDataVariable"/>.</summary>
    public const string DefaultDirectoryName = "Preflight";

    private const string PipelinesDirectoryName = "pipelines";

    private const string MachineStateFileName = "machine.json";

    /// <summary>
    /// Reads the root out of the environment, or refuses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An empty variable counts as absent, which is the rule the CI detection
    /// of <c>Docs/design.md 6.3</c> already applies — an exported-but-empty
    /// variable is a shell artefact, not an instruction.
    /// </para>
    /// <para>
    /// Neither variable set is a named refusal, and not a path built out of
    /// <see langword="null"/>. That state is every container without a Windows
    /// profile, and a <see cref="NullReferenceException"/> escaping to the top
    /// would exit 3 and send the tool's owner to look at somebody's missing
    /// environment.
    /// </para>
    /// <para>
    /// A root that contains, or is, the workspace is refused for the argument
    /// ADR-023 nº5 makes about <c>cachePath</c>, sharpened: the run would load
    /// rule assemblies out of a tree the person running <c>preflight</c> did not
    /// write, which is exactly what <c>Docs/design.md 11.1</c> refuses for the
    /// implicit <c>rules/</c> directory.
    /// </para>
    /// </remarks>
    /// <param name="environment">Where variables are read from.</param>
    /// <param name="workspace">The workspace this run validates.</param>
    /// <exception cref="PolicyValidationException">Neither variable is usable.</exception>
    public static PipelineInstallRoot Resolve(IEnvironmentReader environment, DirectoryInfo workspace)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(workspace);

        var home = NonEmpty(environment.GetVariable(HomeVariable));
        var localAppData = NonEmpty(environment.GetVariable(LocalAppDataVariable));

        var path = home is not null
            ? home
            : localAppData is not null
                ? Path.Combine(localAppData, DefaultDirectoryName)
                : throw Refusal(
                    $"Neither {HomeVariable} nor {LocalAppDataVariable} is set, so there is " +
                    $"nowhere to look for installed pipelines. Set {HomeVariable} to a directory.");

        if (!Path.IsPathFullyQualified(path))
        {
            throw Refusal(
                $"'{path}' in {(home is not null ? HomeVariable : LocalAppDataVariable)} is not an " +
                "absolute path. The install root is a machine location, not a workspace-relative one.");
        }

        var root = new DirectoryInfo(Path.GetFullPath(path));

        if (Contains(root.FullName, workspace.FullName))
        {
            throw Refusal(
                $"The install root '{root.FullName}' contains the workspace " +
                $"'{workspace.FullName}'. Rule assemblies would then be loaded out of the tree " +
                "being validated. Point it somewhere outside.");
        }

        return new PipelineInstallRoot(root);
    }

    private static string? NonEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

    /// <remarks>
    /// The same containment test <c>cachePath</c> uses, and for the same reason
    /// ADR-023 nº5 gives: a path that swallows the workspace turns a read of the
    /// machine's own state into a read of somebody else's checkout. Equality
    /// counts as containment — a root that <em>is</em> the workspace is the
    /// worst case, not an edge one.
    /// </remarks>
    private static bool Contains(string outer, string inner)
    {
        var normalisedOuter = Normalise(outer);
        var normalisedInner = Normalise(inner);

        return normalisedInner.Equals(normalisedOuter, StringComparison.OrdinalIgnoreCase) ||
            normalisedInner.StartsWith(
                normalisedOuter + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static PolicyValidationException Refusal(string message) =>
        new([new PolicyValidationError(message, null, null, HomeVariable)]);

    /// <summary>Where every version of <paramref name="pipeline"/> lives.</summary>
    /// <param name="pipeline">The pipeline name, validated as a label first.</param>
    public DirectoryInfo PipelineDirectory(string pipeline)
    {
        PipelineName.Require(pipeline);

        return new DirectoryInfo(Path.Combine(Root.FullName, PipelinesDirectoryName, pipeline));
    }

    /// <summary>Where one version lives.</summary>
    /// <param name="pipeline">The pipeline name.</param>
    /// <param name="version">The version.</param>
    public DirectoryInfo VersionDirectory(string pipeline, PackageVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new DirectoryInfo(
            Path.Combine(PipelineDirectory(pipeline).FullName, version.ToString()));
    }

    /// <summary>Where the pins and the retention setting are kept.</summary>
    public string MachineStatePath => Path.Combine(Root.FullName, MachineStateFileName);
}
