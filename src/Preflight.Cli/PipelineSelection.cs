namespace Preflight.Cli;

using Preflight.Abstractions;
using Preflight.Core.Policy;

/// <summary>
/// Where the pipeline a run validates against was decided.
/// </summary>
/// <remarks>
/// Carried into the report because a run configured by a file nobody passed
/// must not be indistinguishable from one that was asked for. The local overlay
/// already made that argument in <c>Docs/design.md 6.3</c>, and a pipeline
/// selected off disk is the same class of fact.
/// </remarks>
public enum PipelineSource
{
    /// <summary>No pipeline: the chain starts at <c>preflight.base.json</c>, or nowhere.</summary>
    None,

    /// <summary><c>--pipeline</c>, or its deprecated spelling.</summary>
    CommandLine,

    /// <summary>The <c>pipeline</c> key in <c>preflight.base.json</c>.</summary>
    Checkout,
}

/// <summary>
/// Which pipeline a run uses, and how that was decided.
/// </summary>
/// <param name="Pipeline">The name, or <see langword="null"/> when there is none.</param>
/// <param name="Source">What decided it.</param>
public sealed record PipelineSelection(string? Pipeline, PipelineSource Source);

/// <summary>
/// Resolves the pipeline for one invocation.
/// </summary>
/// <remarks>
/// <c>Docs/design.md 6.2</c> and ADR-029. The flag wins; the checkout key
/// stands in for it; and a workspace holding several pipelines with neither is
/// a refusal rather than a quiet fall back to the base.
/// </remarks>
public static class PipelineSelector
{
    private const string PipelineKey = "pipeline";

    /// <summary>The former spelling, kept accepted by ADR-027.</summary>
    private const string DeprecatedPipelineKey = "production";

    private const string SearchPattern = "preflight.*.json";

    /// <summary>
    /// The files whose names match the pipeline pattern and are not pipelines.
    /// </summary>
    /// <remarks>
    /// Without this list, every workspace in this repository — and every
    /// fixture — becomes an ambiguous selection at once, because all three sit
    /// beside each other at the root and all three match
    /// <c>preflight.*.json</c>.
    /// </remarks>
    public static IReadOnlyList<string> ReservedFileNames { get; } =
    [
        PolicyResolution.BaseFileName,
        PolicyResolution.LocalFileName,
        "preflight.workspace.json",
    ];

    /// <summary>
    /// Decides the pipeline, or refuses.
    /// </summary>
    /// <exception cref="PolicyValidationException">
    /// The choice is ambiguous, or the checkout key is not a usable name.
    /// </exception>
    public static PipelineSelection Select(
        DirectoryInfo workspaceRoot,
        IFileSystem fileSystem,
        string? explicitPipeline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(fileSystem);

        cancellationToken.ThrowIfCancellationRequested();

        // The flag first, and without reading anything: somebody who named a
        // pipeline gets that pipeline, even in a workspace whose base file is
        // unreadable. Validation of the name stays where it was, in
        // PolicyResolution, so both routes reach the same message.
        if (explicitPipeline is not null)
        {
            return new PipelineSelection(explicitPipeline, PipelineSource.CommandLine);
        }

        if (CheckoutKey(workspaceRoot, fileSystem) is { } declared)
        {
            return new PipelineSelection(declared, PipelineSource.Checkout);
        }

        var candidates = Candidates(workspaceRoot, fileSystem);

        // One candidate is not a choice anybody made. Adopting it would be
        // convenient today and a trap the day a second pipeline appears, when
        // the run would silently change what it validates without a single
        // file being edited.
        if (candidates.Count > 1)
        {
            throw Refusal(
                "This workspace holds more than one pipeline and none was chosen: " +
                string.Join(", ", candidates) + ". " +
                $"Pass --pipeline <name>, or declare '{PipelineKey}' in {PolicyResolution.BaseFileName}.",
                null);
        }

        return new PipelineSelection(null, PipelineSource.None);
    }

    /// <summary>
    /// The package version range this checkout accepts, if it declares one.
    /// </summary>
    /// <remarks>
    /// Read from the same file as the pipeline key and in the same way, because
    /// the two belong together: a range that bounds a name nobody declared is
    /// refused rather than quietly bounding nothing. See ADR-032.
    /// </remarks>
    /// <param name="workspaceRoot">The workspace.</param>
    /// <param name="fileSystem">Read access to it.</param>
    /// <param name="pipelineDeclared">Whether the same file names a pipeline.</param>
    public static PipelineRequirement? RequirementOf(
        DirectoryInfo workspaceRoot, IFileSystem fileSystem, bool pipelineDeclared)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var path = Path.Combine(workspaceRoot.FullName, PolicyResolution.BaseFileName);

        if (!fileSystem.FileExists(path))
        {
            return null;
        }

        var document = PolicyDocument.Parse(
            fileSystem.ReadAllTextAsync(path, CancellationToken.None).GetAwaiter().GetResult(), path);

        return PipelineRequirement.Read(
            document, PolicyResolution.BaseFileName, path, pipelineDeclared);
    }

    /// <summary>
    /// The pipeline documents in the workspace root, in ordinal order.
    /// </summary>
    /// <remarks>
    /// Top directory only and sorted, for the reason <c>Docs/design.md 11.1</c>
    /// gives about plugin discovery: a listing whose order comes from the file
    /// system produces a refusal that reads differently on two machines, and a
    /// message nobody can diff is a message nobody trusts.
    /// </remarks>
    public static IReadOnlyList<string> Candidates(DirectoryInfo workspaceRoot, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(fileSystem);

        return
        [
            // Reserved names are dropped before anything is parsed, and the
            // order is load-bearing: preflight.workspace.json holds an array of
            // objects, which a policy document cannot represent, so parsing it
            // raises rather than returning false.
            .. fileSystem
                .EnumerateFiles(workspaceRoot.FullName, SearchPattern, SearchOption.TopDirectoryOnly)
                .Where(path => !ReservedFileNames.Contains(
                    Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                .Where(path => IsPolicyDocument(fileSystem, path))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Whether a file that matches the name pattern is actually a policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name is not enough, and the counter-example ships with .NET:
    /// <c>preflight.deps.json</c> and <c>preflight.runtimeconfig.json</c> sit
    /// beside the executable and match <c>preflight.*.json</c> exactly. A
    /// name-only rule refuses to run in any directory holding them, over
    /// pipelines that do not exist.
    /// </para>
    /// <para>
    /// Extending <see cref="ReservedFileNames"/> instead would be a list that
    /// grows with whatever a runtime emits next, and every entry would be
    /// discovered by somebody hitting it. What a pipeline file <em>is</em> is a
    /// policy document, so that is what is asked.
    /// </para>
    /// <para>
    /// Unreadable or malformed files are skipped rather than raised. This runs
    /// only to decide whether a choice is ambiguous, and a stray file nobody
    /// mentioned must not fail a run that was never going to read it — the
    /// policy loader still refuses it, with a message about the right file, if
    /// anything actually selects it.
    /// </para>
    /// </remarks>
    private static bool IsPolicyDocument(IFileSystem fileSystem, string path)
    {
        try
        {
            var document = PolicyDocument.Parse(
                fileSystem.ReadAllTextAsync(path, CancellationToken.None).GetAwaiter().GetResult(), path);

            return document.TryGetRaw("schemaVersion", out _);
        }
        catch (Exception exception) when (
            exception is System.Text.Json.JsonException
                or System.Diagnostics.UnreachableException
                or IOException)
        {
            // Three ways of saying the same thing. JsonException is malformed
            // JSON; UnreachableException is well-formed JSON holding a shape a
            // policy cannot — an array of objects, for instance, which is what
            // a workspace manifest is; IOException is a file that could not be
            // read at all. None of the three is a pipeline, and none of them is
            // this method's problem to report.
            return false;
        }
    }

    /// <summary>
    /// The pipeline this checkout declares itself to be, if it declares one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The key lives in <c>preflight.base.json</c> rather than in a file of its
    /// own: that file is already versioned, already at the root, and already
    /// read, and inventing a second format to hold one string is a format to
    /// document, parse and refuse.
    /// </para>
    /// <para>
    /// This is a <em>selection</em> read, not a layer. The base document enters
    /// the merge exactly as before — through <c>extends</c>, or not at all —
    /// so no precedence changes.
    /// </para>
    /// </remarks>
    private static string? CheckoutKey(DirectoryInfo workspaceRoot, IFileSystem fileSystem)
    {
        var path = Path.Combine(workspaceRoot.FullName, PolicyResolution.BaseFileName);

        if (!fileSystem.FileExists(path))
        {
            return null;
        }

        // Synchronously, because everything above this is synchronous and the
        // file is one small document read once per invocation. Making the whole
        // selection path async to read it would add a state machine to six
        // commands in exchange for nothing.
        var document = PolicyDocument.Parse(
            fileSystem.ReadAllTextAsync(path, CancellationToken.None).GetAwaiter().GetResult(), path);

        var hasCurrent = document.TryGetRaw(PipelineKey, out var current);
        var hasDeprecated = document.TryGetRaw(DeprecatedPipelineKey, out var deprecated);

        // Refused here rather than left to policy validation, which never sees
        // this document unless the selected pipeline extends it — so a base
        // naming the pipeline twice could otherwise pick a winner in silence.
        if (hasCurrent && hasDeprecated)
        {
            throw Refusal(
                $"'{PipelineKey}' and '{DeprecatedPipelineKey}' are both set in {PolicyResolution.BaseFileName}. " +
                $"'{DeprecatedPipelineKey}' is the deprecated spelling; keep one.",
                path);
        }

        if ((current ?? deprecated) is not string name)
        {
            return null;
        }

        RequireLabel(name, path);

        return name;
    }

    /// <remarks>
    /// The same rule the flag obeys, and for a sharper reason: this name comes
    /// from a versioned file rather than from the person at the keyboard, so
    /// nobody typed it today and nobody is about to notice that it escapes the
    /// workspace.
    /// </remarks>
    private static void RequireLabel(string pipeline, string filePath)
    {
        var valid = pipeline.Length > 0 &&
            pipeline.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

        if (!valid)
        {
            // The file is named in the message text, not only in the error's
            // FilePath: PolicyValidationException composes its message from the
            // messages alone, and this name came from a file rather than from
            // the command line — so "which file" is the first thing the reader
            // needs and the one thing they cannot infer.
            throw Refusal(
                $"'{pipeline}' in {PolicyResolution.BaseFileName} is not a pipeline name. " +
                "Expected letters, digits, '-' or '_'.",
                filePath);
        }
    }

    private static PolicyValidationException Refusal(string message, string? filePath) =>
        new([new PolicyValidationError(message, filePath, null, PipelineKey)]);
}
