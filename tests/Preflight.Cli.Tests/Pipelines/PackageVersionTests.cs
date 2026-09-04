namespace Preflight.Cli.Tests.Pipelines;

using Preflight.Cli.Pipelines;

/// <summary>
/// Fixes what a pipeline package version is, and how two of them order.
/// </summary>
/// <remarks>
/// The ordering test is the one with consequences. Ordinal string comparison is
/// the habit of this codebase and the right answer for rule ids and file names,
/// and it puts <c>1.9.0</c> after <c>1.10.0</c>. "The newest installed version"
/// decided that way resolves the wrong policy with nothing printed about it,
/// which is why a version is three numeric components compared numerically and
/// not a string that happens to sort.
/// </remarks>
public sealed class PackageVersionTests
{
    [Theory]
    [InlineData("1.4")]
    [InlineData("1.4.0.1")]
    [InlineData("v1.4.0")]
    [InlineData("")]
    [InlineData("1.4.0-rc1")]
    [InlineData("1.4.0+build2")]
    [InlineData("1..0")]
    [InlineData("-1.4.0")]
    [InlineData(" 1.4.0")]
    [InlineData("1.4.x")]
    public void TryParse_ForASpellingThatIsNotThreeNumericComponents_IsFalse(string text)
    {
        PackageVersion.TryParse(text, out var version).ShouldBeFalse();
        version.ShouldBeNull();
    }

    [Theory]
    [InlineData("1.4.0")]
    [InlineData("0.0.0")]
    [InlineData("10.20.30")]
    public void TryParse_ForThreeNumericComponents_RoundTripsThroughToString(string text)
    {
        PackageVersion.TryParse(text, out var version).ShouldBeTrue();

        version!.ToString().ShouldBe(text);
    }

    [Theory]
    [InlineData("1.9.0", "1.10.0")]
    [InlineData("1.0.9", "1.0.10")]
    [InlineData("2.0.0", "10.0.0")]
    [InlineData("1.4.0", "1.4.1")]
    public void CompareTo_OrdersNumericallyAndNotOrdinally(string older, string newer)
    {
        Parse(older).CompareTo(Parse(newer)).ShouldBeLessThan(0);
        (Parse(older) < Parse(newer)).ShouldBeTrue();
        (Parse(newer) > Parse(older)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("1.4.0", null, "1.4.0", true)]
    [InlineData("1.4.0", null, "1.3.9", false)]
    [InlineData("1.4.0", null, "9.9.9", true)]
    [InlineData("1.4.0", "2.0.0", "2.0.0", false)]
    [InlineData("1.4.0", "2.0.0", "1.9.9", true)]
    [InlineData("1.4.0", "2.0.0", "1.4.0", true)]
    public void Satisfies_AtTheRangeBoundaries_IsInclusiveBelowAndExclusiveAbove(
        string minimum, string? maximum, string candidate, bool expected)
    {
        var requirement = new PipelineRequirement(
            Parse(minimum), maximum is null ? null : Parse(maximum));

        Parse(candidate).Satisfies(requirement).ShouldBe(expected);
    }

    /// <summary>
    /// A version is newer than nothing at all, and the operators agree with
    /// <see cref="IComparable{T}.CompareTo"/> about it.
    /// </summary>
    /// <remarks>
    /// The null rows are not decoration. Retention orders installed versions and
    /// the resolver picks the newest, and both reach these through
    /// <c>Order()</c> and <c>OrderDescending()</c> — a comparison that put null
    /// on the wrong side would sort a missing version to the top and hand the
    /// run a package that is not there.
    /// </remarks>
    [Fact]
    public void CompareTo_AgainstNothing_IsGreater() =>
        Parse("1.4.0").CompareTo(null).ShouldBeGreaterThan(0);

    [Theory]
    [InlineData("1.4.0", null, false, true)]
    [InlineData(null, "1.4.0", true, false)]
    [InlineData(null, null, false, false)]
    public void TheOperators_WithEitherSideMissing_OrderTheMissingOneFirst(
        string? left, string? right, bool expectedLess, bool expectedGreater)
    {
        var first = left is null ? null : Parse(left);
        var second = right is null ? null : Parse(right);

        (first < second).ShouldBe(expectedLess);
        (first > second).ShouldBe(expectedGreater);
        (first <= second).ShouldBe(expectedLess || (left is null && right is null));
        (first >= second).ShouldBe(expectedGreater || (left is null && right is null));
    }

    /// <remarks>
    /// The four operators are four separate lines of code, and three of them
    /// were reached only through <c>Order()</c>. A theory over the same pair
    /// exercises each one by name, so a copy-paste slip between <c>&gt;</c> and
    /// <c>&gt;=</c> fails here rather than in the retention sweep.
    /// </remarks>
    [Fact]
    public void TheOperators_AtEquality_SeparateStrictFromInclusive()
    {
        var left = Parse("1.4.0");
        var right = Parse("1.4.0");

        (left < right).ShouldBeFalse();
        (left > right).ShouldBeFalse();
        (left <= right).ShouldBeTrue();
        (left >= right).ShouldBeTrue();
    }

    private static PackageVersion Parse(string text)
    {
        PackageVersion.TryParse(text, out var version).ShouldBeTrue();

        return version!;
    }
}
