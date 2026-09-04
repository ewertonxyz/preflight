namespace Preflight.Cli.Tests.Commands;

using System.Reflection;
using Preflight.Cli.Commands;
using Preflight.Core.Policy;

/// <summary>
/// Guards the one screen that answers "where did this number come from".
/// </summary>
/// <remarks>
/// <para>
/// <c>explain</c> renders a <see cref="PolicyOrigin"/> through a switch that
/// ends in a discard. The discard is deliberate and stays: a named arm for the
/// last variant needs an unreachable throw beside it, and an unreachable throw
/// is a branch no test can cover, which trades a real coverage number for a
/// compile-time comfort.
/// </para>
/// <para>
/// The cost of that choice is that a variant added later compiles cleanly and
/// renders as <c>tool default</c> — no error, no failing test, and a wrong
/// answer on the only screen that exists to give the right one. This test is
/// what pays that cost: it enumerates the hierarchy by reflection, so the
/// variant after <c>FromTarget</c> fails here rather than lying in production.
/// </para>
/// </remarks>
public sealed class InspectionCommandHandlersTests
{
    /// <summary>
    /// One sample of every <see cref="PolicyOrigin"/> variant, built by
    /// reflection so the list cannot fall behind the hierarchy.
    /// </summary>
    private static IReadOnlyList<PolicyOrigin> EveryVariant() =>
    [
        new PolicyOrigin.FromFile("preflight.base.json", 5),
        new PolicyOrigin.FromCommandLine(),
        new PolicyOrigin.FromRootKey("defaultTimeoutSeconds", new PolicyOrigin.FromFile("preflight.base.json", 5)),
        new PolicyOrigin.FromTarget("switch2", new PolicyOrigin.FromFile("projectc.json", 12)),
        new PolicyOrigin.FromPackage("projecta", "1.4.0", new PolicyOrigin.FromFile("acme.json", 8)),
        new PolicyOrigin.DescriptorDefault(),
        new PolicyOrigin.ToolDefault(),
    ];

    private static IReadOnlyList<Type> DeclaredVariants() =>
    [
        .. typeof(PolicyOrigin)
            .GetNestedTypes(BindingFlags.Public)
            .Where(type => type is { IsAbstract: false } && typeof(PolicyOrigin).IsAssignableFrom(type)),
    ];

    /// <remarks>
    /// The sample list above is hand-written because the variants take
    /// different constructor arguments. This is what keeps it honest.
    /// </remarks>
    [Fact]
    public void EveryVariant_CoversTheWholeHierarchy() =>
        EveryVariant().Select(origin => origin.GetType()).Distinct()
            .Order(Comparer<Type>.Create((left, right) =>
                string.CompareOrdinal(left.Name, right.Name)))
            .ShouldBe(DeclaredVariants().Order(Comparer<Type>.Create((left, right) =>
                string.CompareOrdinal(left.Name, right.Name))));

    /// <summary>
    /// Only <c>ToolDefault</c> renders as a tool default.
    /// </summary>
    /// <remarks>
    /// The assertion that catches the discard swallowing a new variant: any
    /// origin that is not a tool default and reads as one is reporting a
    /// value's source as somewhere it did not come from.
    /// </remarks>
    [Fact]
    public void Describe_ForEveryPolicyOriginVariant_ProducesADistinctRendering()
    {
        var rendered = EveryVariant()
            .ToDictionary(origin => origin.GetType().Name, InspectionCommandHandlers.DescribeOrigin);

        foreach (var (name, text) in rendered)
        {
            text.ShouldNotBeNullOrWhiteSpace($"{name} renders as nothing.");

            if (name != nameof(PolicyOrigin.ToolDefault))
            {
                text.ShouldNotBe("tool default", $"{name} is being swallowed by the discard.");
            }
        }

        rendered.Values.Distinct(StringComparer.Ordinal).Count().ShouldBe(rendered.Count);
    }

    [Fact]
    public void DescribeOrigin_ForATargetValue_NamesTheTargetKeyAndTheFile()
    {
        var text = InspectionCommandHandlers.DescribeOrigin(
            new PolicyOrigin.FromTarget("switch2", new PolicyOrigin.FromFile("projectc.json", 12)));

        text.ShouldContain("projectc.json:12");
        text.ShouldContain("(target switch2)");
    }
}
