namespace Preflight.Rules;

using System.Text.Json.Serialization;

/// <summary>
/// One setting the version control client has to carry.
/// </summary>
/// <param name="Name">The setting's name, as the client spells it.</param>
/// <param name="Expected">
/// The value it must hold, or <see langword="null"/> when any non-empty value
/// will do.
/// </param>
/// <remarks>
/// The nullable expectation is what lets one shape cover two questions that
/// look different and are not: "this must be turned on" and "this filter must
/// be installed" are both a named setting that has to be there, and the second
/// has no value anybody can predict — an installed filter names a program path
/// that differs on every machine.
///
/// It also decides what a report may print. A check that declared an expected
/// value asked for the comparison and gets it; a check for mere presence gets
/// the setting's name and nothing else, so that a personal address, a remote
/// URL with a credential in it, or a path carrying an account name can never
/// reach a build log through this rule.
/// </remarks>
public sealed record VcsSetting(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("expected")] string? Expected = null);
