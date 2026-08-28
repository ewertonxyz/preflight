namespace Preflight.Core.Tests.Graph;

using Preflight.Core;

/// <summary>
/// Pins the exact public surface of <see cref="RuleGraph"/> against
/// the rule graph.
/// </summary>
/// <remarks>
/// The design specifies three members and no more. This test is what keeps
/// stage selection's execution-set selection out of this type: the obvious place
/// to put it is "one more method on the graph", and doing so would fold
/// stage-awareness and policy-awareness into a type whose whole value is
/// being neither.
/// </remarks>
public sealed class RuleGraphSurfaceTests
{
    [Fact]
    public void RuleGraph_ExposesExactlyLevelsBuildAndTheTwoTraversalMethods()
    {
        typeof(RuleGraph).GetProperties().Select(property => property.Name).ShouldBe(["Levels"]);

        typeof(RuleGraph).GetMethods()
            .Where(method => !method.IsSpecialName && method.DeclaringType == typeof(RuleGraph))
            .Select(method => method.Name)
            .ShouldBe(["Build", "TransitiveDependentsOf", "TransitiveDependenciesOf"], ignoreOrder: true);
    }

    [Fact]
    public void RuleGraph_IsSealed()
    {
        typeof(RuleGraph).IsSealed.ShouldBeTrue();
    }
}
