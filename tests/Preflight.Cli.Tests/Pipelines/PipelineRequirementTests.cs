namespace Preflight.Cli.Tests.Pipelines;

using Preflight.Cli.Pipelines;
using Preflight.Core.Policy;

/// <summary>
/// Fixes the version range a checkout may declare.
/// </summary>
/// <remarks>
/// Every refusal is a configuration error at load, never a requirement that
/// quietly turns out to be absent: a malformed value that were skipped would
/// stop the checkout bounding anything, with nobody told. The range is two keys
/// — an inclusive minimum that is required and an exclusive maximum that is not
/// — rather than an expression like <c>&gt;=1.3 &lt;2.0</c>, because that is a
/// parser to write and test for something two keys already say.
/// </remarks>
public sealed class PipelineRequirementTests
{
    [Fact]
    public void Read_WithoutTheKey_IsNull() =>
        Read("""{ "schemaVersion": 1 }""").ShouldBeNull();

    [Fact]
    public void Read_WithBothBounds_ReadsThem()
    {
        var requirement = Read(
            """{ "requiresPipeline": { "minimumVersion": "1.2.0", "maximumVersion": "2.0.0" } }""");

        requirement!.Minimum.ToString().ShouldBe("1.2.0");
        requirement.Maximum!.ToString().ShouldBe("2.0.0");
    }

    [Fact]
    public void Read_WithOnlyAMinimum_LeavesTheUpperBoundOpen() =>
        Read("""{ "requiresPipeline": { "minimumVersion": "1.2.0" } }""")!.Maximum.ShouldBeNull();

    /// <remarks>
    /// A range open below says "any version ever published", which is not a
    /// bound and is indistinguishable from having written no key at all.
    /// </remarks>
    [Theory]
    [InlineData("""{ "requiresPipeline": { } }""")]
    [InlineData("""{ "requiresPipeline": { "maximumVersion": "2.0.0" } }""")]
    public void Read_WithoutAMinimum_ThrowsNamingTheMissingMember(string json) =>
        Should.Throw<PolicyValidationException>(() => Read(json))
            .Message.ShouldContain("minimumVersion");

    [Theory]
    [InlineData("""{ "requiresPipeline": 5 }""")]
    [InlineData("""{ "requiresPipeline": "1.4.0" }""")]
    [InlineData("""{ "requiresPipeline": [] }""")]
    public void Read_WithANonObjectValue_ThrowsRatherThanRequiringNothing(string json) =>
        Should.Throw<PolicyValidationException>(() => Read(json));

    [Fact]
    public void Read_WithAVersionThatIsNotThreeNumbers_Throws() =>
        Should.Throw<PolicyValidationException>(
            () => Read("""{ "requiresPipeline": { "minimumVersion": "1.4" } }"""));

    [Fact]
    public void Read_WithAMinimumNotBelowTheMaximum_ThrowsNamingBoth()
    {
        var error = Should.Throw<PolicyValidationException>(() => Read(
            """{ "requiresPipeline": { "minimumVersion": "2.0.0", "maximumVersion": "2.0.0" } }"""));

        error.Message.ShouldContain("2.0.0");
        error.Message.ShouldContain("exclusive");
    }

    /// <remarks>
    /// A range that bounds a name nobody declared bounds nothing.
    /// </remarks>
    [Fact]
    public void Read_WithoutAPipelineDeclared_ThrowsNamingTheKeyItNeeds() =>
        Should.Throw<PolicyValidationException>(() => Read(
            """{ "requiresPipeline": { "minimumVersion": "1.0.0" } }""",
            pipelineDeclared: false))
            .Message.ShouldContain("pipeline");

    /// <summary>
    /// A bound that is not a string is refused rather than read as absent.
    /// </summary>
    /// <remarks>
    /// The dangerous shape is <c>"minimumVersion": 1.4</c>, which somebody
    /// writes by leaving off the quotes and which JSON is perfectly happy with.
    /// Treated as a missing member it would produce a requirement with no lower
    /// bound — a checkout that believes it is pinned to a range and accepts
    /// anything ever published.
    /// </remarks>
    [Theory]
    [InlineData("minimumVersion", "1.4")]
    [InlineData("minimumVersion", "true")]
    [InlineData("maximumVersion", "2")]
    [InlineData("maximumVersion", "[]")]
    public void Read_WithABoundThatIsNotAString_ThrowsRatherThanTreatingItAsAbsent(
        string member, string value)
    {
        var json = member == "minimumVersion"
            ? $$"""{ "requiresPipeline": { "minimumVersion": {{value}} } }"""
            : $$"""{ "requiresPipeline": { "minimumVersion": "1.0.0", "maximumVersion": {{value}} } }""";

        Should.Throw<PolicyValidationException>(() => Read(json))
            .Message.ShouldContain(member);
    }

    private static PipelineRequirement? Read(string json, bool pipelineDeclared = true) =>
        PipelineRequirement.Read(
            PolicyDocument.Parse(json, "preflight.base.json"),
            "preflight.base.json",
            null,
            pipelineDeclared);
}
