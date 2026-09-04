namespace Preflight.TestSupport;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Builds descriptor sets for the tests that render a graph or a rule table.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than beside the graph tests because two projects need it. The
/// graph tests own <c>GraphFixture</c>, which is <c>internal</c> to
/// <c>Preflight.Core.Tests</c> and therefore unreachable from
/// <c>Preflight.Cli.Tests</c> — where the SARIF reporter and the DOT renderer
/// live. Copying it would give the two projects their own idea of what a
/// descriptor set looks like, which is the same drift that put
/// <see cref="RunResultFixture"/> here rather than beside one of them.
/// </para>
/// <para>
/// Everything is keyed by <see cref="RuleId"/>, never by
/// <see cref="RuleDescriptor"/>: two descriptors built by two calls with
/// equal-content <c>DependsOn</c> lists are not equal to each other.
/// </para>
/// <para>
/// <see cref="Documented"/> is a separate name rather than an overload of
/// <see cref="Rule(string, string[])"/>, and deliberately so. With both called
/// <c>Rule</c>, C# overload resolution prefers the member with more declared
/// parameters when both are applicable only in expanded form — so
/// <c>Rule("core.a.left", "core.a.top")</c> would silently bind the dependency
/// as documentation and produce a root node. A fixture that quietly builds a
/// different graph from the one the test reads is worse than no fixture.
/// </para>
/// </remarks>
public static class RuleDescriptorFixture
{
    public static RuleDescriptor Rule(string id, params string[] dependsOn) =>
        Documented(id, null, dependsOn);

    /// <summary>
    /// A rule whose <see cref="RuleDescriptor.Documentation"/> is set, which is
    /// what the SARIF <c>helpUri</c> is derived from.
    /// </summary>
    public static RuleDescriptor Documented(string id, string? documentation, params string[] dependsOn) => new()
    {
        Id = new RuleId(id),
        DisplayName = id,
        Stage = ValidationStage.BuildReadiness,
        DependsOn = [.. dependsOn.Select(dependency => new RuleId(dependency))],
        Documentation = documentation,
    };

    /// <summary>
    /// The three descriptors of the run
    /// <see cref="RunResultFixture.DocumentedExample"/> renders, in the order
    /// that run executes them.
    /// </summary>
    /// <remarks>
    /// The ids match that fixture's executions exactly, because a reporter that
    /// needs a <c>DisplayName</c> looks the descriptor up by id and a set that
    /// half-matched would exercise the fallback rather than the feature. The
    /// dependency is the one the run's skip attribution already claims:
    /// <c>compile-probe</c> was skipped because <c>configuration</c> failed.
    /// </remarks>
    public static RuleDescriptor[] ForDocumentedExample() =>
    [
        Documented("core.workspace.toolchain", "https://wiki/preflight/rules/toolchain"),
        Rule("core.build.configuration"),
        Rule("core.build.compile-probe", "core.build.configuration"),
    ];
}
