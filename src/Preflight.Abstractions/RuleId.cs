namespace Preflight.Abstractions;

using System.Text.RegularExpressions;

/// <summary>
/// The primary key of a validation rule.
/// </summary>
/// <remarks>
/// Every reference to a rule that crosses a process boundary — policy files,
/// <c>--set</c> arguments, the NDJSON history, the SARIF <c>ruleId</c>, the
/// documentation URL — goes through this type. Validating in the constructor,
/// rather than normalising, means <c>Core.Foo</c> and <c>core.foo</c> are never
/// silently treated as the same key: the second one simply never exists.
/// </remarks>
public readonly record struct RuleId
{
    private static readonly Regex Pattern = new(
        @"^[a-z0-9]+(\.[a-z0-9]+(-[a-z0-9]+)*){2,}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Value { get; }

    public RuleId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!Pattern.IsMatch(value))
        {
            throw new ArgumentException(
                $"Rule id '{value}' is invalid. Expected lowercase '<scope>.<area>.<name>', " +
                "for example 'core.presubmit.large-file'.",
                nameof(value));
        }

        Value = value;
    }

    public override string ToString() => Value;
}
