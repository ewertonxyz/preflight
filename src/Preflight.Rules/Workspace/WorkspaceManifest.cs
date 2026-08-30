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
    /// </remarks>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
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
