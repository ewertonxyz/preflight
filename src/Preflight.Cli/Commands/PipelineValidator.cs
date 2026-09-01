namespace Preflight.Cli.Commands;

using Preflight.Abstractions.Rules;
using Preflight.Core;
using Preflight.Core.Plugins;
using Preflight.Core.Policy;

/// <summary>
/// Raised when a pipeline source tree does not hold together.
/// </summary>
/// <remarks>
/// Carries every problem, not the first one. A configuration error, so it exits
/// 2 through the one mapping that decides exit codes: a tree that does not hold
/// together is the author's file, not a defect in this tool.
/// </remarks>
public sealed class PipelineValidationException : ConfigurationLoadException
{
    public PipelineValidationException(IReadOnlyList<string> problems)
        : base(string.Join(Environment.NewLine, problems)) => Problems = problems;

    /// <summary>Everything wrong with the tree, found in one pass.</summary>
    public IReadOnlyList<string> Problems { get; }
}

/// <summary>
/// <c>preflight pipeline validate</c>.
/// </summary>
/// <remarks>
/// <para>
/// The manifest, the assemblies it names and the policy document it carries,
/// loaded together and reported together. Until this command existed the three
/// were only ever checked as a side effect of something else — the policy by
/// <c>rules</c>, the assemblies by a run, the manifest by an install on
/// somebody else's machine — so an author found out about the second problem
/// after fixing the first, and about the third from a colleague.
/// </para>
/// <para>
/// Accumulation is the whole feature, and it is the promise policy loading
/// already makes one layer down (<c>Docs/design.md 6.1</c>): every error across
/// every document, together, because a tree with four problems should take one
/// edit to fix rather than four runs to discover.
/// </para>
/// <para>
/// One consequence worth naming rather than hiding: when an assembly fails to
/// load, the rule ids it would have declared are reported as unknown by the
/// policy check that follows. In a run that ordering is refused, because the
/// run aborts and the misleading message is the only one anybody sees. Here the
/// load failure is two lines above it in the same output, which is exactly the
/// context that makes the second message readable instead of misleading.
/// </para>
/// </remarks>
public static class PipelineValidator
{
    /// <summary>
    /// Loads a source tree's manifest, assemblies and policy together.
    /// </summary>
    /// <param name="environment">Where the file system, the loader and the console are.</param>
    /// <param name="directory">The tree to validate.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <exception cref="PipelineValidationException">The tree has at least one problem.</exception>
    /// <exception cref="PackageManifestException">The tree's manifest cannot be read at all.</exception>
    public static async Task<int> ValidateAsync(
        CommandEnvironment environment,
        string directory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(directory);

        var tree = new DirectoryInfo(Path.GetFullPath(directory));

        if (!tree.Exists)
        {
            throw new PipelineValidationException(
                [$"There is no directory at {tree.FullName} to validate."]);
        }

        // The one thing that cannot be accumulated with anything else: it names
        // the policy file and the assemblies, so nothing below it has a subject
        // without it. Its own refusal is already precise, and is re-raised
        // rather than reworded.
        var manifest = InstalledPipelineReader.Read(
            Path.Combine(tree.FullName, PackageManifest.FileName));

        var problems = new List<string>();

        CheckContract(manifest, problems);
        CheckWorkspaceManifest(tree, problems);

        var rules = LoadRules(environment, tree, manifest, problems);

        await CheckPolicyAsync(environment, tree, manifest, rules, problems, cancellationToken);

        if (problems.Count > 0)
        {
            throw new PipelineValidationException(problems);
        }

        environment.Console.Output.WriteLine(
            $"{manifest.Name}@{manifest.Version} in {tree.FullName} validates.");
        environment.Console.Output.WriteLine(
            $"{rules.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} rules " +
            $"visible, policy '{manifest.PolicyFile}' loads clean.");

        return ExitCode.Success;
    }

    /// <remarks>
    /// The same question <c>install</c> asks, asked at the moment the one
    /// person who can fix it is looking. A package whose assemblies need a
    /// contract this build does not provide installs nowhere, and finding that
    /// out from a hundred machines at once is the outcome ADR-033 refuses.
    /// </remarks>
    private static void CheckContract(PackageManifest manifest, List<string> problems)
    {
        if (!Version.TryParse(manifest.AbstractionsMinimumVersion, out var required))
        {
            problems.Add(
                $"'{manifest.AbstractionsMinimumVersion}' is not a contract version. " +
                $"Expected the version of {AbstractionsCompatibility.AssemblyName} these rules build against.");

            return;
        }

        if (!AbstractionsCompatibility.IsCompatible(required, AbstractionsCompatibility.HostVersion))
        {
            problems.Add(
                $"These rules need {AbstractionsCompatibility.AssemblyName} " +
                $"{manifest.AbstractionsMinimumVersion}, and this build provides " +
                $"{AbstractionsCompatibility.HostVersion.ToString()}. Nothing would install this package.");
        }
    }

    /// <remarks>
    /// The check <c>pack</c> makes, made where an author can act on it without
    /// having produced an archive first. Any depth, ignoring case, as
    /// <c>ReservedFileNames</c> already treats that name.
    /// </remarks>
    private static void CheckWorkspaceManifest(DirectoryInfo tree, List<string> problems)
    {
        foreach (var path in Directory.EnumerateFiles(
            tree.FullName, "preflight.workspace.json", SearchOption.AllDirectories))
        {
            problems.Add(
                $"'{Path.GetRelativePath(tree.FullName, path).Replace('\\', '/')}' is a workspace " +
                "manifest. It describes one checkout, and pack refuses to ship it.");
        }
    }

    /// <remarks>
    /// The assemblies the manifest names, loaded through the loader a run uses.
    /// Anything short of that would be a second opinion about what loads, and
    /// two opinions is how a package validates here and fails there.
    /// </remarks>
    private static IReadOnlyList<IValidationRule> LoadRules(
        CommandEnvironment environment,
        DirectoryInfo tree,
        PackageManifest manifest,
        List<string> problems)
    {
        var directories = new List<string>();

        foreach (var named in manifest.RuleAssemblies)
        {
            var path = Path.Combine(
                tree.FullName, named.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(path))
            {
                problems.Add(
                    $"The manifest names '{named}', and the tree does not hold it. " +
                    "A package that installs without an assembly it declares runs a smaller set " +
                    "of checks than its author published.");

                continue;
            }

            var parent = Path.GetDirectoryName(path)!;

            if (!directories.Contains(parent, StringComparer.OrdinalIgnoreCase))
            {
                directories.Add(parent);
            }
        }

        using var loader = environment.AssemblyLoader();

        try
        {
            // The executable's own rules/ directory is deliberately out of
            // reach: this validates a tree, and a plugin sitting beside the
            // binary would make the answer depend on the machine running the
            // command rather than on the tree being checked.
            var probe = PluginPathResolution.Resolve(
                environment.FileSystem, tree, NoImplicitRules.Value, directories);

            return new PluginLoader(loader).Load(environment.Rules, probe);
        }
        catch (PluginLoadException exception)
        {
            problems.AddRange(exception.Errors.Select(error => error.Message));

            return environment.Rules;
        }
    }

    private static async Task CheckPolicyAsync(
        CommandEnvironment environment,
        DirectoryInfo tree,
        PackageManifest manifest,
        IReadOnlyList<IValidationRule> rules,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        var policyPath = Path.Combine(
            tree.FullName, manifest.PolicyFile.Replace('/', Path.DirectorySeparatorChar));

        if (!environment.FileSystem.FileExists(policyPath))
        {
            problems.Add(
                $"The manifest names '{manifest.PolicyFile}' as the policy document, and the tree " +
                "does not hold it. That file is the whole of what the package configures.");

            return;
        }

        var descriptors = rules.Select(rule => rule.Descriptor).ToArray();

        try
        {
            var loaded = await new PolicyLoader(environment.FileSystem)
                .LoadAsync(policyPath, cancellationToken);

            problems.AddRange(
                PolicyValidator.ValidateAll(loaded.Documents, descriptors)
                    .Concat(PolicyValidator.ValidateSeals(
                        PolicySeal.Parse(loaded.Documents),
                        loaded.Documents,
                        local: null,
                        overrides: [],
                        descriptors))
                    .Select(error => error.Message));
        }
        catch (ConfigurationLoadException exception)
        {
            // Everything the loader raises is already a message an author can
            // act on — an extends that escapes, a document that will not parse,
            // a schema version nothing understands. Rewording it here would
            // produce two vocabularies for one file.
            problems.Add(exception.Message);
        }
    }

    /// <summary>
    /// A directory that holds no plugins, so the implicit probe finds nothing.
    /// </summary>
    /// <remarks>
    /// Created once and never written to, on the same terms as the empty
    /// directory the command tests share. Pointing the implicit probe at the
    /// executable would let a plugin installed on this machine decide whether
    /// somebody else's tree validates.
    /// </remarks>
    private static readonly Lazy<DirectoryInfo> NoImplicitRules =
        new(() => Directory.CreateTempSubdirectory("preflight-validate-no-plugins-"));
}
