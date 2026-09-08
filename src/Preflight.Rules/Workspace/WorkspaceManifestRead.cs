namespace Preflight.Rules;

using System.Text.Json;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// The workspace manifest as a rule sees it: where it was looked for, what was
/// in it, and — when it would not parse — the finding to report instead of it.
/// </summary>
/// <remarks>
/// <para>
/// Three rules read the same file, and each used to carry its own copy of the
/// path resolution and of the "not valid JSON" finding. Three copies of one
/// message are three chances for one file to be described two ways in one
/// report: whoever improves the remediation in <see cref="ToolchainRule"/>
/// leaves <see cref="DependenciesRule"/> saying something else about the same
/// syntax error, and the reader is left deciding which of the two to believe.
/// </para>
/// <para>
/// A collaborator the rules hold, rather than a base class they extend. What
/// they share is how the manifest is found and what a broken one is called;
/// what they do with an absent one is exactly where they disagree —
/// <see cref="ToolchainRule"/> fails, because a mistyped path that reported
/// <c>NotApplicable</c> would be green forever, and the other two report
/// <c>NotApplicable</c>, because the toolchain rule they depend on has already
/// said it. A base class would have to hold both answers to serve both callers.
/// </para>
/// </remarks>
internal sealed record WorkspaceManifestRead(
    string ManifestPath,
    string RelativePath,
    WorkspaceManifest? Manifest,
    Finding? Malformed)
{
    /// <summary>
    /// Resolves the path the policy asks for and reads whatever is there.
    /// </summary>
    /// <remarks>
    /// A manifest that is simply absent is not an error here. It arrives as a
    /// <see langword="null"/> <see cref="Manifest"/> with no
    /// <see cref="Malformed"/> beside it, and each rule decides for itself what
    /// that means.
    /// </remarks>
    public static async Task<WorkspaceManifestRead> ReadAsync(
        RuleContext context,
        CancellationToken cancellationToken)
    {
        var relativePath = context.Policy.GetValue("manifestPath", WorkspaceManifest.DefaultFileName);
        var manifestPath = Path.Combine(context.WorkspaceRoot.FullName, relativePath);

        try
        {
            return new WorkspaceManifestRead(
                manifestPath,
                relativePath,
                await WorkspaceManifest.LoadAsync(context.FileSystem, manifestPath, cancellationToken),
                Malformed: null);
        }
        catch (JsonException exception)
        {
            return new WorkspaceManifestRead(
                manifestPath,
                relativePath,
                Manifest: null,
                NotValidJson(relativePath, exception));
        }
    }

    /// <remarks>
    /// The message names the syntax and the file, and the remediation offers
    /// the second possibility as well: a manifest that will not parse is
    /// sometimes not the manifest at all, but a policy pointing
    /// <c>manifestPath</c> at some other JSON file that was never meant to be
    /// one.
    ///
    /// The path is the one relative to the workspace root, like every other
    /// rule's. An absolute path carries the name of the account that ran the
    /// tool, and this message reaches a build log that many more people read
    /// than ran it.
    /// </remarks>
    private static Finding NotValidJson(string manifestPath, JsonException exception) => new()
    {
        Message = "The workspace manifest is not valid JSON.",
        Location = new FindingLocation(manifestPath),
        Actual = exception.Message,
        Remediation =
            "Fix the syntax, or ask the pipeline's author to point 'manifestPath' " +
            "at the right file.",
    };
}
