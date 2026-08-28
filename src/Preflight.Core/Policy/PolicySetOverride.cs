namespace Preflight.Core.Policy;

using Preflight.Abstractions;

/// <summary>
/// One already-typed <c>--set</c> override: a rule id (or none, for a root
/// key), a dotted path, and the value already resolved to its CLR type by the
/// caller.
/// </summary>
/// <remarks>
/// Parsing the command-line flag itself — the <c>:</c> separator, the greedy
/// id-prefix form, ambiguous-prefix detection — is the CLI's concern in
/// <c>Preflight.Cli</c>. This type is what that parsing eventually produces,
/// and <see cref="ToNode"/> is what lets it enter
/// <see cref="PolicyNode.Merge"/> through the exact same path a JSON layer
/// does, rather than a second, separate merge implementation.
/// </remarks>
public sealed record PolicySetOverride
{
    public required RuleId? RuleId { get; init; }

    public required string Path { get; init; }

    public required object? TypedValue { get; init; }

    public PolicyNode ToNode()
    {
        PolicyNode node = new PolicyNode.Leaf(
            PolicyValue.Initial(TypedValue, new PolicyOrigin.FromCommandLine()));

        foreach (var segment in Path.Split('.').Reverse())
        {
            node = new PolicyNode.ObjectNode(new Dictionary<string, PolicyNode> { [segment] = node });
        }

        if (RuleId is { } ruleId)
        {
            node = new PolicyNode.ObjectNode(new Dictionary<string, PolicyNode>
            {
                ["rules"] = new PolicyNode.ObjectNode(new Dictionary<string, PolicyNode>
                {
                    [ruleId.Value] = node,
                }),
            });
        }

        return node;
    }
}
