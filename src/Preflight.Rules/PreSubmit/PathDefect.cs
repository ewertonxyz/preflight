namespace Preflight.Rules;

/// <summary>
/// One thing wrong with one path, as a check found it.
/// </summary>
/// <param name="Path">The path, relative to the workspace root.</param>
/// <param name="Problem">What is wrong with it, in the check's own words.</param>
/// <param name="Expected">What a portable path would have looked like instead.</param>
/// <remarks>
/// Not a <see cref="Preflight.Abstractions.Model.Finding"/>, and the difference
/// is the whole reason this type exists. A check knows what it looked at and
/// what it objected to; it does not know how the reader is supposed to fix it,
/// because the remedy depends on which limits the policy set and on how the
/// rule chose to phrase them. Letting a check write the remediation would put
/// four voices in one report.
/// </remarks>
internal sealed record PathDefect(string Path, string Problem, string Expected);
