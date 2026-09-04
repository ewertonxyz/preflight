namespace Preflight.Core.Policy;

internal sealed record PolicyKeyDefinition(
    string Name,
    PolicyValueKind Kind,
    IReadOnlyList<string>? AllowedValues = null,
    PolicyValueRange? Range = null);
