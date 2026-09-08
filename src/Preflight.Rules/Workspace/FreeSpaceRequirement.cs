namespace Preflight.Rules;

using System.Text.Json.Serialization;

/// <summary>
/// One volume the workspace needs room on.
/// </summary>
/// <param name="Path">A path, relative to the workspace root, on the volume to measure.</param>
/// <param name="MinimumBytes">The fewest available bytes that count as enough.</param>
/// <remarks>
/// A path rather than a drive letter or a mount point, because the workspace
/// knows where it writes and not which volume that lands on — a build output
/// folder is frequently a junction onto another disk, and the declaration that
/// names the folder stays right when somebody moves it.
/// </remarks>
public sealed record FreeSpaceRequirement(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("minimumBytes")] long MinimumBytes);
