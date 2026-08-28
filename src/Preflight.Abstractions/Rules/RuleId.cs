namespace Preflight.Abstractions.Rules;

using System.Text.RegularExpressions;

/// <summary>
/// The primary key of a validation rule.
/// </summary>
/// <remarks>
/// <para>
/// Every reference to a rule that crosses a process boundary — policy files,
/// <c>--set</c> arguments, the NDJSON history, the SARIF <c>ruleId</c>, the
/// documentation URL — goes through this type. Validating in the constructor,
/// rather than normalising, means <c>Core.Foo</c> and <c>core.foo</c> are never
/// silently treated as the same key: the second one simply never exists.
/// </para>
/// <para>
/// The pattern is source-generated rather than built at runtime. Every
/// invocation of the tool constructs one of these per rule in the policy before
/// it does anything else, and a runtime-compiled regex pays for emitting IL on
/// the first match — work a process that lives for a few seconds never earns
/// back. The generator moves that cost to build time and leaves the matching
/// speed where it was.
/// </para>
/// </remarks>
public readonly partial record struct RuleId
{
    public string Value { get; }

    public RuleId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!Pattern().IsMatch(value))
        {
            throw new ArgumentException(
                $"Rule id '{value}' is invalid. Expected lowercase '<scope>.<area>.<name>', " +
                "for example 'core.presubmit.large-file'.",
                nameof(value));
        }

        Value = value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(
        @"^[a-z0-9]+(\.[a-z0-9]+(-[a-z0-9]+)*){2,}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
