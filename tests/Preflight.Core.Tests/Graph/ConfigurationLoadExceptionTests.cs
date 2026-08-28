namespace Preflight.Core.Tests.Graph;

using Preflight.Abstractions;
using Preflight.Core;
using Preflight.Core.Policy;

/// <summary>
/// Fixes the one thing the two families of configuration error must share: a
/// single base the exit-code logic can catch.
/// </summary>
/// <remarks>
/// the load-time flow sends both an invalid policy and an invalid
/// graph to the same destination — exit 2 at load time, never at execution
/// time. They carry different payloads: a policy error knows a file and a
/// line, a dependency cycle defined in compiled descriptors has neither. So
/// they stay separate types with a common abstract base, rather than one type
/// whose file and line are always null for half its uses.
///
/// A marker interface would not do the job: C# has no <c>catch (IFoo)</c>, so
/// the caller would be back to <c>catch (Exception ex) when (ex is IFoo)</c>
/// — two things to remember instead of one.
/// </remarks>
public sealed class ConfigurationLoadExceptionTests
{
    [Fact]
    public void GraphValidationException_Message_JoinsAllErrorMessagesWithNewline()
    {
        var exception = new GraphValidationException([
            new GraphValidationError.SelfDependency(new RuleId("core.a.alpha")),
            new GraphValidationError.DuplicateRuleId(new RuleId("core.a.bravo")),
        ]);

        exception.Message.ShouldContain("core.a.alpha");
        exception.Message.ShouldContain("core.a.bravo");
        exception.Message.ShouldContain(Environment.NewLine);
    }

    [Fact]
    public void GraphValidationException_IsAConfigurationLoadException()
    {
        typeof(GraphValidationException).IsSubclassOf(typeof(ConfigurationLoadException)).ShouldBeTrue();
    }

    [Fact]
    public void PolicyValidationException_IsAConfigurationLoadException()
    {
        typeof(PolicyValidationException).IsSubclassOf(typeof(ConfigurationLoadException)).ShouldBeTrue();
    }

    /// <remarks>
    /// Asserts the point in the form the caller will actually use it, not just
    /// as a type-hierarchy fact: the CLI's exit-code selection gets one
    /// <c>catch</c> clause for every way configuration can be wrong.
    /// </remarks>
    [Fact]
    public void ConfigurationLoadException_BothConcreteSubtypes_AreCaughtByOneCommonCatchClause()
    {
        CaughtAsConfigurationLoad(() => throw new GraphValidationException([
            new GraphValidationError.SelfDependency(new RuleId("core.a.alpha")),
        ])).ShouldBeTrue();

        CaughtAsConfigurationLoad(() => throw new PolicyValidationException([
            new PolicyValidationError("boom", "atlas.json", 1, "rules"),
        ])).ShouldBeTrue();
    }

    private static bool CaughtAsConfigurationLoad(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ConfigurationLoadException)
        {
            return true;
        }
    }
}
