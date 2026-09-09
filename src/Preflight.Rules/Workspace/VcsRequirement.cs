namespace Preflight.Rules;

using System.Text.Json.Serialization;

/// <summary>
/// What the version control client has to be configured to do.
/// </summary>
/// <param name="Command">
/// The client to ask. Empty when the workspace declares none, in which case the
/// rules fall back to the one they name as their default.
/// </param>
/// <param name="Settings">The settings this workspace requires.</param>
/// <remarks>
/// The command is declared rather than written into the rule for the same
/// reason the toolchain entries declare theirs: which client a repository uses
/// is a fact about the repository, and a rule with the name compiled into it
/// would be the one place the tool assumed an answer it asks for everywhere
/// else.
/// </remarks>
public sealed record VcsRequirement(
    [property: JsonPropertyName("command")] string? Command = null,
    [property: JsonPropertyName("settings")] IReadOnlyList<VcsSetting>? Settings = null);
