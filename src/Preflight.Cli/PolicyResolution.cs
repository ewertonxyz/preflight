namespace Preflight.Cli;

using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Core.Policy;

/// <summary>
/// The policy for one run, and everything a report needs to explain it.
/// </summary>
/// <param name="Policy">The fully resolved policy.</param>
/// <param name="Chain">
/// The files that composed it, in application order, plus the local overlay
/// when it applied.
/// </param>
/// <param name="Overlay">Whether the local overlay took part, and why.</param>
/// <param name="Selection">Which pipeline was used, and what decided it.</param>
/// <param name="Package">
/// The installed package the policy came from, or <see langword="null"/> when
/// none took part.
/// </param>
public sealed record ResolvedPolicy(
    EffectivePolicy Policy,
    IReadOnlyList<string> Chain,
    LocalOverlayDecision Overlay,
    PipelineSelection Selection,
    InstalledPipeline? Package = null);

/// <summary>
/// Assembles the policy precedence chain.
/// </summary>
/// <remarks>
/// <para>
/// The layers, weakest first: rule descriptor defaults, the pipeline document
/// and its <c>extends</c> ancestors, the local overlay, then the <c>--set</c>
/// overrides. The order is the whole of the merge, and getting it wrong does
/// not throw — it produces a run configured differently from what the files
/// say, reported as a success.
/// </para>
/// <para>
/// Validation happens here, before anything executes, which puts every
/// configuration problem at load time, and every error found is reported
/// together rather than one per run: a policy with four unknown keys should
/// take one edit to fix, not four runs to discover.
/// </para>
/// </remarks>
public static class PolicyResolution
{
    public const string BaseFileName = "preflight.base.json";

    public const string LocalFileName = "preflight.local.json";

    /// <summary>
    /// The file a named pipeline's overlay lives in.
    /// </summary>
    /// <remarks>
    /// The name is interpolated into a filename, so it is validated as a label
    /// first. <c>--pipeline</c> comes straight from the user, and a name
    /// containing a separator would read a file outside the workspace.
    /// </remarks>
    public static string PipelineFileName(string pipeline) => $"preflight.{pipeline}.json";

    public static async Task<ResolvedPolicy> ResolveAsync(
        DirectoryInfo workspaceRoot,
        IFileSystem fileSystem,
        IEnvironmentReader environment,
        IReadOnlyList<RuleDescriptor> descriptors,
        RunOptions options,
        CancellationToken cancellationToken,
        InstalledPipeline? package = null)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(options);

        // Before anything is loaded, because it decides what gets loaded. All
        // six commands that resolve a policy reach this method, so the checkout
        // key reaches all six rather than only run.
        var selection = PipelineSelector.Select(
            workspaceRoot, fileSystem, options.Pipeline, cancellationToken);

        var loader = new PolicyLoader(fileSystem);

        // A resolved package replaces the workspace file as the entry of the
        // chain. It does not become an ancestor the checkout may extend and
        // override: if it did, `sealed` would be the only thing standing between
        // a project and the studio's limits, and every project would re-litigate
        // the baseline the previous phase spent an ADR settling. The local
        // overlay is still the escape hatch, and it is still suppressed in CI.
        var entryPath = package is not null
            ? Path.Combine(package.Root.FullName, PackagePolicyFileName(package))
            : EntryPath(workspaceRoot, fileSystem, selection.Pipeline);

        PolicyDocument? pipeline = null;
        IReadOnlyList<string> chain = [];
        IReadOnlyList<PolicyDocument> documents = [];

        if (entryPath is not null)
        {
            var loaded = await loader.LoadAsync(entryPath, cancellationToken);

            pipeline = package is null
                ? loaded.Document
                : PackageProvenance.Qualify(loaded.Document, package);

            chain = package is null
                ? loaded.Chain
                : [.. loaded.Chain.Select(path => PackageProvenance.Describe(package, path))];

            documents = package is null
                ? loaded.Documents
                : [.. loaded.Documents.Select(document =>
                    PackageProvenance.Qualify(document, package))];
        }

        var localPath = Path.Combine(workspaceRoot.FullName, LocalFileName);
        var overlay = LocalOverlay.Decide(
            environment,
            options.NoLocal,
            options.AllowLocal,
            fileSystem.FileExists(localPath));

        PolicyDocument? local = null;

        if (overlay.Applied)
        {
            local = (await loader.LoadAsync(localPath, cancellationToken)).Document;
            chain = [.. chain, localPath];
        }

        var overrides = options.SetOverrides
            .Select(argument => SetOverrideParser.Parse(argument, [.. descriptors.Select(d => d.Id)]))
            .ToArray();

        Validate(documents, pipeline, local, overrides, descriptors);

        return new ResolvedPolicy(
            EffectivePolicy.Build(descriptors, pipeline, local, overrides, options.Target),
            chain,
            overlay,
            selection,
            package);
    }

    /// <summary>
    /// The policy document inside a package, as its manifest names it.
    /// </summary>
    /// <remarks>
    /// Read from the manifest rather than assumed to be
    /// <c>preflight.&lt;name&gt;.json</c>: the packager decides the layout, and a
    /// convention guessed here would be a second place that has to agree with
    /// it.
    /// </remarks>
    public static string PackagePolicyFileName(InstalledPipeline package)
    {
        ArgumentNullException.ThrowIfNull(package);

        return InstalledPipelineReader
            .Read(Path.Combine(package.Root.FullName, PackageManifest.FileName))
            .PolicyFile;
    }

    /// <summary>
    /// Which file the chain starts from, or <see langword="null"/> when there
    /// is none.
    /// </summary>
    /// <remarks>
    /// A named pipeline whose file is absent is exit 2, never a silent
    /// fallback to the base. Falling back runs a weaker set of checks than the
    /// pipeline asked for and calls it a success — the false green of
    /// principle 7, produced by a typo in a CI argument.
    /// </remarks>
    private static string? EntryPath(DirectoryInfo workspaceRoot, IFileSystem fileSystem, string? pipeline)
    {
        if (pipeline is not null)
        {
            PipelineName.Require(pipeline);

            var path = Path.Combine(workspaceRoot.FullName, PipelineFileName(pipeline));

            return path;
        }

        var basePath = Path.Combine(workspaceRoot.FullName, BaseFileName);

        // No base file is not an error: a workspace can be validated on
        // descriptor defaults alone. The console header says 'defaults only' so
        // that a run which looks configured and is not cannot be mistaken for
        // one that is.
        //
        // Through the injected file system, not File.Exists. Every other read in
        // this method already goes through the seam, and one direct call is
        // enough to make a command that resolves a policy untestable without a
        // real directory — which is what measure and report would have inherited.
        return fileSystem.FileExists(basePath) ? basePath : null;
    }

    /// <remarks>
    /// Every layer is validated, including the <c>--set</c> overrides — which
    /// sit at the top of the precedence chain and would otherwise be the least
    /// checked thing in it.
    /// </remarks>
    private static void Validate(
        IReadOnlyList<PolicyDocument> chain,
        PolicyDocument? pipeline,
        PolicyDocument? local,
        IReadOnlyList<PolicySetOverride> overrides,
        IReadOnlyList<RuleDescriptor> descriptors)
    {
        var documents = new[] { pipeline, local }.OfType<PolicyDocument>().ToArray();
        var errors = new List<PolicyValidationError>(PolicyValidator.ValidateAll(documents, descriptors));

        foreach (var setOverride in overrides)
        {
            errors.AddRange(PolicyValidator.ValidateSetOverride(setOverride, descriptors));
        }

        // The seals last, and from the unmerged chain: a seal is about the
        // relationship between layers, so it cannot be checked against the one
        // document the merge produced.
        errors.AddRange(
            PolicyValidator.ValidateSeals(PolicySeal.Parse(chain), chain, local, overrides, descriptors));

        if (errors.Count > 0)
        {
            throw new PolicyValidationException(errors);
        }
    }
}
