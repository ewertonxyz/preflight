namespace Preflight.Core.Tests.Policy;

using Preflight.Abstractions.Model;
using Preflight.Core.Policy;

/// <summary>
/// Fixes the grammar of a <c>targets</c> key and what it applies to.
/// </summary>
/// <remarks>
/// The grammar is two strings and a separator, deliberately. There is no glob,
/// for the reason <c>compileProbe.inputs</c> has none: a pattern language is a
/// parser to write and test before two strings can be compared.
/// </remarks>
public sealed class PolicyTargetKeyTests
{
    private static StatedBuildTarget Stated(
        string platform, string configuration, bool platformStated = true, bool configurationStated = true) =>
        new(new BuildTarget(platform, configuration), platformStated, configurationStated);

    [Theory]
    [InlineData("win64", "win64", null, 1)]
    [InlineData("ps5|Shipping", "ps5", "Shipping", 2)]
    [InlineData("switch2|Debug", "switch2", "Debug", 2)]
    public void TryParse_WithAWellFormedKey_SucceedsWithTheExpectedSpecificity(
        string text, string platform, string? configuration, int specificity)
    {
        PolicyTargetKey.TryParse(text, out var key).ShouldBeTrue();

        key.Platform.ShouldBe(platform);
        key.Configuration.ShouldBe(configuration);
        key.Specificity.ShouldBe(specificity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("|Shipping")]
    [InlineData("win64|")]
    [InlineData("win64|A|B")]
    [InlineData("win64*")]
    public void TryParse_WithAMalformedKey_FailsAndYieldsTheDefault(string text)
    {
        PolicyTargetKey.TryParse(text, out var key).ShouldBeFalse();

        key.ShouldBe(default);
    }

    /// <summary>
    /// The word the tool uses for "no platform given" is not a platform.
    /// </summary>
    /// <remarks>
    /// <c>--platform</c> falls back to <c>any</c>, so a block keyed on it reads
    /// as a wildcard and would behave as the literal string. Refusing it at
    /// load costs one message; accepting it costs somebody a day.
    /// </remarks>
    [Theory]
    [InlineData("any")]
    [InlineData("ANY")]
    [InlineData("any|Shipping")]
    public void TryParse_WithTheUnstatedPlatformWord_IsRefused(string text) =>
        PolicyTargetKey.TryParse(text, out _).ShouldBeFalse();

    /// <remarks>
    /// The value reaches this from a human at a keyboard and from a CI yaml, so
    /// the comparison is ordinal and case-insensitive — and the validator
    /// refuses two keys in one document that differ only in case, because they
    /// are then the same key with two spellings.
    /// </remarks>
    [Theory]
    [InlineData("PS5", "ps5", true)]
    [InlineData("ps5", "PS5", true)]
    [InlineData("ps5", "win64", false)]
    public void Matches_IsOrdinalCaseInsensitive(string keyText, string platform, bool expected)
    {
        PolicyTargetKey.TryParse(keyText, out var key).ShouldBeTrue();

        key.Matches(Stated(platform, "Shipping")).ShouldBe(expected);
    }

    [Fact]
    public void Matches_WithAPlatformOnlyKey_IgnoresTheConfiguration()
    {
        PolicyTargetKey.TryParse("ps5", out var key).ShouldBeTrue();

        key.Matches(Stated("ps5", "Shipping")).ShouldBeTrue();
        key.Matches(Stated("ps5", "Debug")).ShouldBeTrue();
    }

    [Fact]
    public void Matches_WithAPlatformAndConfigurationKey_RequiresBoth()
    {
        PolicyTargetKey.TryParse("ps5|Shipping", out var key).ShouldBeTrue();

        key.Matches(Stated("ps5", "Shipping")).ShouldBeTrue();
        key.Matches(Stated("ps5", "Debug")).ShouldBeFalse();
        key.Matches(Stated("win64", "Shipping")).ShouldBeFalse();
    }

    /// <summary>
    /// An axis the user did not state matches nothing.
    /// </summary>
    /// <remarks>
    /// This is the whole of the decision. <c>--configuration</c> falls back to
    /// <c>Development</c>, so a <c>win64|Development</c> block would otherwise
    /// fire on a run that said only <c>--platform win64</c> — handing somebody
    /// one configuration's thresholds because they omitted a flag, and calling
    /// it a pass. The tool refuses rather than assumes, everywhere, and an
    /// unstated axis is the case that makes the difference visible.
    /// </remarks>
    [Fact]
    public void Matches_WithAnAxisTheUserDidNotState_IsFalse()
    {
        PolicyTargetKey.TryParse("win64|Development", out var pair).ShouldBeTrue();
        PolicyTargetKey.TryParse("win64", out var platformOnly).ShouldBeTrue();

        var configurationDefaulted = Stated("win64", "Development", configurationStated: false);

        pair.Matches(configurationDefaulted).ShouldBeFalse();
        platformOnly.Matches(configurationDefaulted).ShouldBeTrue();

        platformOnly.Matches(StatedBuildTarget.Unstated).ShouldBeFalse();
    }

    /// <remarks>
    /// The companion to the theory above: <c>Development</c> is a legitimate
    /// configuration to target, and refusing the word outright would forbid the
    /// commonest block anyone would write. What makes it safe is that the axis
    /// has to have been stated, not that the value is banned.
    /// </remarks>
    [Fact]
    public void Matches_WithADevelopmentConfigurationTheUserStated_IsTrue()
    {
        PolicyTargetKey.TryParse("win64|Development", out var key).ShouldBeTrue();

        key.Matches(Stated("win64", "Development")).ShouldBeTrue();
    }
}
