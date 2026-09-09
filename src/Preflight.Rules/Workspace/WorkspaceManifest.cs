namespace Preflight.Rules;

using System.Text.Json;
using System.Text.Json.Serialization;
using Preflight.Abstractions.Services;

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

    /// <remarks>
    /// Comments and trailing commas are allowed, matching what the policy
    /// schema grants policy files. A manifest is edited by the same people
    /// under the same conditions, and a format that rejects a trailing comma
    /// teaches everyone to distrust the error message.
    ///
    /// A key nobody recognises is refused, which the policy schema has always
    /// done and this file used to ignore. Ignoring it is the quietest failure
    /// the tool can produce: a workspace declaring <c>enviroment</c> gets a
    /// rule that reports it checked nothing, forever, with no message anywhere
    /// saying why. The refusal arrives as the same "not valid JSON" finding a
    /// syntax error does, which already names the file and offers the other
    /// possibility.
    /// </remarks>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

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
    /// The volumes this workspace needs room on, and how much.
    /// </summary>
    /// <remarks>
    /// In the manifest rather than in policy, like the tools and the
    /// dependencies beside it: how much room a build needs is a fact about this
    /// repository, not a decision about how strictly it is validated.
    /// </remarks>
    [JsonPropertyName("freeSpace")]
    public IReadOnlyList<FreeSpaceRequirement> FreeSpace { get; init; } = [];

    /// <summary>
    /// How the version control client has to be configured for this workspace.
    /// </summary>
    /// <remarks>
    /// A fact about the repository rather than a decision about how strictly it
    /// is validated, like everything else here: a repository holding paths
    /// longer than the Windows limit needs long paths turned on wherever it is
    /// cloned, and that does not change with the production overlay in force.
    /// </remarks>
    [JsonPropertyName("vcs")]
    public VcsRequirement? Vcs { get; init; }

    /// <summary>
    /// The environment variables this workspace needs set.
    /// </summary>
    /// <remarks>
    /// Names alone, with no way to declare what a value should look like, and
    /// that is a decision rather than a shape nobody got round to enriching. A
    /// rule that could be told what a value must match would have to quote the
    /// value it found in order to explain itself, and every string a finding
    /// carries reaches a build log that far more people read than ran the
    /// build. With names alone the rule has nothing to print, and the guarantee
    /// is structural instead of being a comment somebody has to keep obeying.
    /// </remarks>
    [JsonPropertyName("environment")]
    public IReadOnlyList<string> Environment { get; init; } = [];

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
}
