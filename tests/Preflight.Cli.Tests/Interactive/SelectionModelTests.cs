namespace Preflight.Cli.Tests.Interactive;

using Preflight.Cli.Interactive;
using Preflight.Cli.Pipelines;

/// <summary>
/// Fixes what a picker is given, without a terminal anywhere near it.
/// </summary>
/// <remarks>
/// The model carries everything asserted about this feature. Rendering belongs
/// to Spectre.Console and is deliberately untested — asserting on the escape
/// sequences it emits would be asserting about Spectre.Console — so everything
/// that could be wrong about a menu has to be visible here: the order, the
/// labels, which row is current and which row would produce a workable state.
/// </remarks>
public sealed class SelectionModelTests
{
    private static PackageVersion Version(string text)
    {
        PackageVersion.TryParse(text, out var version).ShouldBeTrue();

        return version!;
    }

    [Fact]
    public void ForPipelines_OrdersOrdinallyAndMarksNothingActive()
    {
        var model = SelectionModel.ForPipelines(["projectb", "projecta"]);

        model.Choices.Select(choice => choice.Value).ShouldBe(["projecta", "projectb"]);
        model.Choices.ShouldAllBe(choice => choice.IsAllowed && !choice.IsActive);
        model.ActiveIndex.ShouldBe(0);
        model.Prompt.ShouldNotBeEmpty();
    }

    /// <remarks>
    /// Newest first, because the answer somebody is most often looking for is
    /// the one they just installed. The ordering is numeric rather than
    /// ordinal, which is the assertion with the largest single consequence in
    /// this phase: ordinal puts 1.9.0 after 1.10.0 and the menu then offers the
    /// wrong version at the top.
    /// </remarks>
    [Fact]
    public void ForVersions_OrdersNumericallyNewestFirst()
    {
        var model = SelectionModel.ForVersions(
            "projecta",
            [Version("1.9.0"), Version("1.10.0"), Version("1.0.9")],
            pinned: null,
            requirement: null);

        model.Choices.Select(choice => choice.Value)
            .ShouldBe(["projecta@1.10.0", "projecta@1.9.0", "projecta@1.0.9"]);
    }

    [Fact]
    public void ForVersions_StartsTheCursorOnThePinnedRowAndSaysSoInItsLabel()
    {
        var model = SelectionModel.ForVersions(
            "projecta",
            [Version("1.4.0"), Version("2.0.0")],
            pinned: Version("1.4.0"),
            requirement: null);

        model.ActiveIndex.ShouldBe(1);
        model.Choices[1].IsActive.ShouldBeTrue();
        model.Choices[1].Label.ShouldContain("pinned");
        model.Choices[0].IsActive.ShouldBeFalse();
    }

    /// <remarks>
    /// Shown and marked, never removed. A version the checkout will not accept
    /// is still on the disk and still pinnable, and a list shorter than the
    /// directory it describes leaves somebody looking for a version they can
    /// see in a file manager with nothing saying where it went.
    /// </remarks>
    [Fact]
    public void ForVersions_WithARange_MarksTheOnesOutsideItWithoutRemovingThem()
    {
        var model = SelectionModel.ForVersions(
            "projecta",
            [Version("1.4.0"), Version("2.0.0")],
            pinned: null,
            requirement: new PipelineRequirement(Version("1.0.0"), Version("2.0.0")));

        model.Choices.Count.ShouldBe(2);

        var outside = model.Choices.Single(choice => choice.Value == "projecta@2.0.0");

        outside.IsAllowed.ShouldBeFalse();
        outside.Label.ShouldContain("outside the range");

        model.Choices.Single(choice => choice.Value == "projecta@1.4.0").IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public void ForVersions_WithNothingInstalled_IsAnEmptyModelRatherThanAThrow()
    {
        var model = SelectionModel.ForVersions("projecta", [], pinned: null, requirement: null);

        model.Choices.ShouldBeEmpty();
        model.ActiveIndex.ShouldBe(0);
    }
}
