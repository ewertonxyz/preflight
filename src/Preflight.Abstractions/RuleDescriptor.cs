namespace Preflight.Abstractions;

/// <summary>
/// The static description of a validation rule.
/// </summary>
/// <remarks>
/// Every <c>Default</c>-prefixed member is only a default; policy has the final
/// word on each one for a given production.
/// </remarks>
public sealed record RuleDescriptor
{
    public required RuleId Id { get; init; }

    public required string DisplayName { get; init; }

    public required ValidationStage Stage { get; init; }

    public IReadOnlyList<RuleId> DependsOn { get; init; } = [];

    public Severity DefaultSeverity { get; init; } = Severity.Error;

    public bool DefaultBlocking { get; init; } = true;

    public bool DefaultGating { get; init; } = true;

    public int DefaultTimeoutSeconds { get; init; } = 60;

    public string? Documentation { get; init; }
}
