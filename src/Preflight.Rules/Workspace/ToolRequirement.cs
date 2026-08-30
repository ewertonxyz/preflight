namespace Preflight.Rules;

using System.Text.Json.Serialization;

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
