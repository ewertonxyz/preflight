namespace Preflight.Abstractions;

/// <summary>
/// A single piece of evidence a rule reports.
/// </summary>
/// <remarks>
/// <see cref="Expected"/> and <see cref="Actual"/> are
/// separated from <see cref="Message"/> so a reporter can format a readable
/// diff without parsing free text.
///
/// Deliberately has no <c>Severity</c>: severity is a property of the rule, not
/// of an individual finding, and belongs to policy. Every finding of a rule
/// inherits the rule's effective severity.
/// </remarks>
public sealed record Finding
{
    public required string Message { get; init; }

    public FindingLocation? Location { get; init; }

    public string? Expected { get; init; }

    public string? Actual { get; init; }

    public string? Remediation { get; init; }
}

public sealed record FindingLocation(string RelativePath, int? Line = null, int? Column = null);
