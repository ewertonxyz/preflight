namespace Preflight.Abstractions.Model;

/// <summary>
/// Where in the workspace a <see cref="Finding"/> points.
/// </summary>
/// <remarks>
/// The path is relative to the workspace root, never absolute. A finding is
/// read on a machine other than the one that produced it — in a report, in
/// SARIF, in a code review — and an absolute path names a directory that
/// reader does not have.
/// </remarks>
public sealed record FindingLocation(string RelativePath, int? Line = null, int? Column = null);
