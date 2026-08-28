namespace Preflight.Core.Tests.Graph;

using Preflight.Abstractions;

/// <summary>
/// Builds descriptor sets for the graph tests.
/// </summary>
/// <remarks>
/// Shared rather than repeated privately in each file so that every graph test
/// describes its topology the same way, and so the one rule that matters is
/// stated once: everything here is keyed by <see cref="RuleId"/>, never by
/// <see cref="RuleDescriptor"/>. Two descriptors built by two calls with
/// equal-content <c>DependsOn</c> lists are not equal to each other — see
/// <c>RuleDescriptorTests</c> — so a graph that indexed by descriptor would
/// misbehave in ways no test would obviously explain.
/// </remarks>
internal static class GraphFixture
{
    public static RuleDescriptor Rule(string id, params string[] dependsOn) =>
        Rule(id, ValidationStage.PreSubmit, dependsOn);

    public static RuleDescriptor Rule(string id, ValidationStage stage, params string[] dependsOn) => new()
    {
        Id = new RuleId(id),
        DisplayName = id,
        Stage = stage,
        DependsOn = [.. dependsOn.Select(dependency => new RuleId(dependency))],
    };

    /// <summary>
    /// A linear chain <c>chain.rule.n0</c> → <c>n1</c> → … → <c>n(depth-1)</c>,
    /// where each node depends on the next.
    /// </summary>
    public static RuleDescriptor[] Chain(int depth) =>
        [.. Enumerable.Range(0, depth).Select(i =>
            i == depth - 1 ? Rule(ChainId(i)) : Rule(ChainId(i), ChainId(i + 1)))];

    public static string ChainId(int index) => $"chain.rule.n{index}";

    public static RuleId[] IdsOf(IEnumerable<string> ids) => [.. ids.Select(id => new RuleId(id))];
}
