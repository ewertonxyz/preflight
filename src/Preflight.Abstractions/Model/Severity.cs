namespace Preflight.Abstractions.Model;

/// <summary>
/// The severity a rule runs at. Owned by policy, never by the rule itself.
/// </summary>
public enum Severity
{
    Information,
    Warning,
    Error,
}
