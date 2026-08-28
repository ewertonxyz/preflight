namespace Preflight.Core.Tests;

using Preflight.Abstractions.Rules;

/// <summary>
/// Fixes the shape and validation of <see cref="RuleId"/> before anything else
/// in the rule contract is built on top of it.
/// </summary>
/// <remarks>
/// The rule-id contract: <c>RuleId</c> is the primary key of everything external to the
/// process — policy files, <c>--set</c> arguments, the NDJSON history, the
/// SARIF <c>ruleId</c>, and the documentation URL. A validation gap here does
/// not surface as a local defect; it surfaces as an unrelated "unknown rule"
/// error in a policy file that looks correct.
/// </remarks>
public sealed class RuleIdTests
{
    [Theory]
    [InlineData("core.presubmit.large-file")]
    [InlineData("core.build.compile-probe")]
    [InlineData("a.b.c")]
    [InlineData("a.b.c.d")]
    [InlineData("123.456.789")]
    public void Constructor_WithValidValue_SetsValueAndToString(string value)
    {
        var ruleId = new RuleId(value);

        ruleId.Value.ShouldBe(value);
        ruleId.ToString().ShouldBe(value);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("a.b")]
    [InlineData("A.b.c")]
    [InlineData("a.B.c")]
    [InlineData("a.b_c.d")]
    [InlineData(".a.b.c")]
    [InlineData("a.b.c.")]
    [InlineData("a..b.c")]
    [InlineData("a.-b.c")]
    [InlineData("a.b-.c")]
    [InlineData("a.b--c.d")]
    [InlineData("-a.b.c")]
    [InlineData("a.b.c ")]
    public void Constructor_WithInvalidFormat_ThrowsArgumentExceptionNamingTheValue(string value)
    {
        var exception = Should.Throw<ArgumentException>(() => new RuleId(value));

        exception.Message.ShouldContain($"'{value}'");
        exception.Message.ShouldContain("core.presubmit.large-file");
        exception.ParamName.ShouldBe("value");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Constructor_WithNullOrWhitespace_ThrowsViaThrowIfNullOrWhiteSpace(string? value)
    {
        var exception = Should.Throw<ArgumentException>(() => new RuleId(value!));

        exception.ParamName.ShouldBe("value");
        exception.Message.ShouldNotContain("is invalid. Expected lowercase");
    }

    [Fact]
    public void RuleId_HasStructuralEquality_SuitableForUseAsADictionaryKey()
    {
        var first = new RuleId("core.presubmit.large-file");
        var second = new RuleId("core.presubmit.large-file");

        first.ShouldBe(second);
        (first == second).ShouldBeTrue();
        first.GetHashCode().ShouldBe(second.GetHashCode());

        var lookup = new Dictionary<RuleId, int> { [first] = 42 };

        lookup[new RuleId("core.presubmit.large-file")].ShouldBe(42);
    }
}
