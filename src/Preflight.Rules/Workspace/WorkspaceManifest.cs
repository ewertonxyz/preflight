namespace Preflight.Rules;

using System.Text.Json;
using System.Text.Json.Serialization;
using Preflight.Abstractions;

/// <summary>
/// What the workspace declares it needs.
/// </summary>
/// <remarks>
/// <para>
/// Both workspace rules check what "the manifest" declares, and never says what
/// the manifest is. This is it, and the shape is deliberately the smallest one
/// that answers the two questions they ask — which tools, at which versions,
/// and which dependencies.
/// </para>
/// <para>
/// It is a file rather than policy on purpose. Policy separates the rule from
/// the production's configuration of it; what a workspace <em>needs</em> is
/// neither. A team switching to a newer SDK changes a fact about the
/// repository, not a decision about how strictly it is validated, and putting
/// that in policy would mean every production overlay carried a copy of it.
/// </para>
/// </remarks>
public sealed record WorkspaceManifest
{
    /// <summary>
    /// The file the workspace rules read, relative to the workspace root.
    /// </summary>
    public const string DefaultFileName = "preflight.workspace.json";

    [JsonPropertyName("tools")]
    public IReadOnlyList<ToolRequirement> Tools { get; init; } = [];

    [JsonPropertyName("dependencies")]
    public IReadOnlyList<DependencyRequirement> Dependencies { get; init; } = [];

    /// <summary>
    /// How to compile-probe this workspace, if it can be probed at all.
    /// </summary>
    /// <remarks>
    /// Here rather than in policy for the same reason the tools are: how a
    /// workspace is compiled is a fact about the workspace, not a decision
    /// about how strictly it is validated.
    /// </remarks>
    [JsonPropertyName("compileProbe")]
    public CompileProbe? CompileProbe { get; init; }

    /// <summary>
    /// Reads and parses the manifest.
    /// </summary>
    /// <returns>
    /// The manifest, or <see langword="null"/> when the file is not there.
    /// </returns>
    /// <exception cref="JsonException">The file is not valid JSON.</exception>
    public static async Task<WorkspaceManifest?> LoadAsync(
        IFileSystem fileSystem,
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        if (!fileSystem.FileExists(path))
        {
            return null;
        }

        var json = await fileSystem.ReadAllTextAsync(path, cancellationToken);

        return JsonSerializer.Deserialize<WorkspaceManifest>(json, Options) ?? new WorkspaceManifest();
    }

    /// <remarks>
    /// Comments and trailing commas are allowed, matching what the policy
    /// schema grants policy files. A manifest is edited by the same people
    /// under the same conditions, and a format that rejects a trailing comma
    /// teaches everyone to distrust the error message.
    /// </remarks>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

/// <summary>
/// One tool the workspace needs, and the versions it accepts.
/// </summary>
/// <param name="Name">How the tool is named in a report.</param>
/// <param name="Command">The executable to run.</param>
/// <param name="Arguments">What makes it print its version.</param>
/// <param name="MinimumVersion">Lowest accepted version, inclusive.</param>
/// <param name="MaximumVersion">First rejected version, exclusive.</param>
/// <remarks>
/// Two explicit bounds rather than a range expression. "The version satisfies
/// the range" names no syntax, and every syntax that exists — npm's, NuGet's,
/// SemVer's — is a grammar with its own parser, its own precedence rules and
/// its own edge cases, all of which would have to be implemented and tested
/// here before a single version could be compared. Two bounds express the same
/// intent, cannot be written ambiguously, and need no parser at all.
///
/// The upper bound is exclusive because that is what a major-version ceiling
/// means in practice: "anything in 10.x" is <c>10.0.0</c> to <c>11.0.0</c>, and
/// an inclusive bound would need a version nobody can write.
/// </remarks>
public sealed record ToolRequirement(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("arguments")] IReadOnlyList<string> Arguments,
    [property: JsonPropertyName("minimumVersion")] string? MinimumVersion = null,
    [property: JsonPropertyName("maximumVersion")] string? MaximumVersion = null);

/// <summary>
/// One dependency the workspace declares.
/// </summary>
/// <param name="Id">The package or module name.</param>
/// <param name="Version">The version declared for it.</param>
/// <param name="RestoredMarker">
/// A path, relative to the workspace root, that exists once the dependency has
/// been restored.
/// </param>
/// <remarks>
/// A marker path rather than a query to a package manager, because the tool
/// forbids the tool from fetching anything: asking a feed whether a version
/// exists is a network call, and one that turns a validation run into something
/// that behaves differently on a train. What is on disk is checkable, offline,
/// in microseconds — and it is the fact that actually decides whether a build
/// will work.
/// </remarks>
public sealed record DependencyRequirement(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version")] string? Version = null,
    [property: JsonPropertyName("restoredMarker")] string? RestoredMarker = null);

/// <summary>
/// The command that compiles without linking.
/// </summary>
/// <param name="Command">The executable to run.</param>
/// <param name="Arguments">
/// Its arguments. Any occurrence of <c>{probeOutput}</c> is replaced with a
/// path outside the workspace for the probe to write into.
/// </param>
/// <param name="WorkingDirectory">
/// Where to run it, relative to the workspace root. Defaults to the root.
/// </param>
/// <param name="Inputs">
/// Everything the probe reads, as paths relative to the workspace root. A
/// directory contributes every file under it. Absent means the probe is never
/// cached.
/// </param>
/// <remarks>
/// The <c>{probeOutput}</c> token exists because of a non-goal: the tool never
/// writes to the workspace, and the engine runs rules at the same level
/// concurrently — but a compiler writes intermediates wherever it is told, and
/// told nothing it writes next to the sources. The read-only
/// <see cref="IFileSystem"/> cannot prevent that: the rule does not do the
/// writing, the child process does. The token is how a manifest sends the
/// output somewhere else, and the integration layer asserts the fixture is
/// byte-identical after a probe.
///
/// <para>
/// <c>Inputs</c> exists for the incremental cache, and it is the one part of
/// this manifest that can be wrong in a way the tool cannot detect. The engine
/// does not know what a compiler reads, and inferring it was rejected precisely
/// because an inferred set errs by optimism. So the workspace declares it — the
/// same arrangement as <c>minimumVersion</c> and <c>restoredMarker</c>, where
/// what the workspace needs is stated by the workspace.
/// </para>
/// <para>
/// The consequence has to be said plainly: a declaration that leaves out a
/// directory the compiler reads will serve a cached <c>Passed</c> after a
/// change in that directory. Omitting <c>Inputs</c> entirely is therefore the
/// default and the safe state — no declaration, no caching. A directory rather
/// than a glob for the same reason this manifest takes two version bounds
/// instead of a range syntax: every glob dialect is a parser to write and test
/// before a single file is compared.
/// </para>
/// </remarks>
public sealed record CompileProbe(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("arguments")] IReadOnlyList<string> Arguments,
    [property: JsonPropertyName("workingDirectory")] string? WorkingDirectory = null,
    [property: JsonPropertyName("inputs")] IReadOnlyList<string>? Inputs = null);
