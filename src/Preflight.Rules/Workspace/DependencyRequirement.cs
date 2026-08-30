namespace Preflight.Rules;

using System.Text.Json.Serialization;

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
/// never fetches anything. Asking a feed whether a version exists is a network
/// call, and a network call turns a validation run into something that answers
/// differently on a train, behind a proxy, or on the morning the feed is down.
/// What is on disk is checkable offline in microseconds — and it is also the
/// fact that actually decides whether a build will work, since a package the
/// feed has and the machine does not still fails to compile.
/// </remarks>
public sealed record DependencyRequirement(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version")] string? Version = null,
    [property: JsonPropertyName("restoredMarker")] string? RestoredMarker = null);
