namespace Preflight.Cli.Tests;

using NSubstitute;

/// <summary>
/// Fixes the local-overlay table of the local-overlay rule and the
/// CI detection it depends on.
/// </summary>
/// <remarks>
/// This is an integrity control, not a convenience: <c>preflight.local.json</c>
/// is unversioned, nothing stops a <c>"blocking": false</c> from being left in
/// it, and the local-overlay rule says trusting nobody to forget works until gold week. A
/// defect here does not throw — it quietly relaxes a rule inside CI.
/// </remarks>
public sealed class LocalOverlayTests
{
    private static IEnvironmentReader EnvironmentWith(params (string Name, string? Value)[] variables)
    {
        var environment = Substitute.For<IEnvironmentReader>();

        environment.GetVariable(Arg.Any<string>()).Returns((string?)null);

        foreach (var (name, value) in variables)
        {
            environment.GetVariable(name).Returns(value);
        }

        return environment;
    }

    [Theory]
    [InlineData("CI")]
    [InlineData("TEAMCITY_VERSION")]
    [InlineData("GITHUB_ACTIONS")]
    [InlineData("BUILD_BUILDID")]
    [InlineData("JENKINS_URL")]
    public void DetectCi_WithAKnownVariableSet_ReturnsItsName(string name)
    {
        LocalOverlay.DetectCi(EnvironmentWith((name, "1"))).ShouldBe(name);
    }

    /// <remarks>
    /// Detection is <em>present and non-empty</em>. An automation server
    /// that exports a variable without a value is not announcing CI, and the
    /// distinction is invisible to a test that only ever sets "1".
    /// </remarks>
    [Theory]
    [InlineData("CI")]
    [InlineData("TEAMCITY_VERSION")]
    [InlineData("GITHUB_ACTIONS")]
    [InlineData("BUILD_BUILDID")]
    [InlineData("JENKINS_URL")]
    public void DetectCi_WithAKnownVariablePresentButEmpty_ReturnsNull(string name)
    {
        LocalOverlay.DetectCi(EnvironmentWith((name, ""))).ShouldBeNull();
    }

    [Fact]
    public void DetectCi_WithNothingSet_ReturnsNull()
    {
        LocalOverlay.DetectCi(EnvironmentWith()).ShouldBeNull();
    }

    /// <remarks>
    /// Reads backwards and is correct: <c>CI=false</c> is present and non-empty,
    /// so the local-overlay rule makes it CI. Pinned here because the next reader will
    /// assume otherwise and "fix" it.
    /// </remarks>
    [Fact]
    public void DetectCi_WithCiSetToTheStringFalse_StillReportsCi()
    {
        LocalOverlay.DetectCi(EnvironmentWith(("CI", "false"))).ShouldBe("CI");
    }

    /// <remarks>
    /// Two variables at once is the common case on GitHub Actions, which sets
    /// both <c>CI</c> and <c>GITHUB_ACTIONS</c>. The explain command prints which one
    /// was detected, so a varying enumeration order breaks that line
    /// intermittently — the failure mode principle 1 is worst at.
    /// </remarks>
    [Fact]
    public void DetectCi_WithTwoVariablesSet_PicksTheFirstInDeclaredOrder()
    {
        LocalOverlay.DetectCi(EnvironmentWith(("GITHUB_ACTIONS", "true"), ("CI", "true"))).ShouldBe("CI");
    }

    [Fact]
    public void Decide_OutsideCiWithNoFlags_AppliesTheOverlay()
    {
        var decision = LocalOverlay.Decide(EnvironmentWith(), noLocal: false, allowLocal: false, fileExists: true);

        decision.Applied.ShouldBeTrue();
        decision.Suppressed.ShouldBe(LocalOverlaySuppression.None);
        decision.CiVariable.ShouldBeNull();
    }

    [Fact]
    public void Decide_InsideCi_DoesNotApplyTheOverlay_AndNamesTheVariable()
    {
        var decision = LocalOverlay.Decide(
            EnvironmentWith(("TEAMCITY_VERSION", "2024.1")),
            noLocal: false,
            allowLocal: false,
            fileExists: true);

        decision.Applied.ShouldBeFalse();
        decision.Suppressed.ShouldBe(LocalOverlaySuppression.CiDetected);
        decision.CiVariable.ShouldBe("TEAMCITY_VERSION");
    }

    [Fact]
    public void Decide_WithNoLocal_DoesNotApplyTheOverlay()
    {
        var decision = LocalOverlay.Decide(EnvironmentWith(), noLocal: true, allowLocal: false, fileExists: true);

        decision.Applied.ShouldBeFalse();
        decision.Suppressed.ShouldBe(LocalOverlaySuppression.ExplicitlyDisabled);
    }

    /// <remarks>
    /// The local-overlay rule: <c>--allow-local</c> wins inside CI, and exists to debug CI
    /// locally.
    /// </remarks>
    [Fact]
    public void Decide_WithAllowLocalInsideCi_AppliesTheOverlay()
    {
        var decision = LocalOverlay.Decide(
            EnvironmentWith(("CI", "1")),
            noLocal: false,
            allowLocal: true,
            fileExists: true);

        decision.Applied.ShouldBeTrue();
        decision.Suppressed.ShouldBe(LocalOverlaySuppression.None);
    }

    /// <remarks>
    /// The variable is still reported when <c>--allow-local</c> overrode it.
    /// A run that forced the overlay on inside CI is precisely the run whose
    /// header needs to say so.
    /// </remarks>
    [Fact]
    public void Decide_WithAllowLocalInsideCi_StillNamesTheCiVariable()
    {
        var decision = LocalOverlay.Decide(
            EnvironmentWith(("CI", "1")),
            noLocal: false,
            allowLocal: true,
            fileExists: true);

        decision.CiVariable.ShouldBe("CI");
    }

    /// <remarks>
    /// No file is a separate outcome from suppression. The console header of
    /// the local-overlay rule has to distinguish "the overlay was ignored" from "there was
    /// nothing to ignore"; collapsing them would announce an integrity decision
    /// that was never made.
    /// </remarks>
    [Fact]
    public void Decide_OutsideCiWithNoFile_ReportsTheFileAbsentRatherThanSuppression()
    {
        var decision = LocalOverlay.Decide(EnvironmentWith(), noLocal: false, allowLocal: false, fileExists: false);

        decision.Applied.ShouldBeFalse();
        decision.Suppressed.ShouldBe(LocalOverlaySuppression.FileAbsent);
    }

    /// <remarks>
    /// <c>--no-local</c> is honoured even when there is no file, so the reported
    /// reason stays the user's instruction and not an accident of the working
    /// directory.
    /// </remarks>
    [Fact]
    public void Decide_WithNoLocalAndNoFile_ReportsTheExplicitDisable()
    {
        var decision = LocalOverlay.Decide(EnvironmentWith(), noLocal: true, allowLocal: false, fileExists: false);

        decision.Suppressed.ShouldBe(LocalOverlaySuppression.ExplicitlyDisabled);
    }

    /// <summary>
    /// The one test that touches the real environment, so that the adapter
    /// every other test substitutes away is itself known to work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything above proves the <em>decision</em> against a fake. None of it
    /// proves the production reader ever reads anything — an implementation
    /// returning <see langword="null"/> unconditionally would leave all of them
    /// green and make CI detection fail open, applying a local overlay inside
    /// CI, which is the one outcome the local-overlay rule exists to prevent.
    /// </para>
    /// <para>
    /// It mutates process-wide state, which is why the variable name is unique
    /// to this test and the value is cleared in a <c>finally</c>. xUnit v3
    /// parallelises test classes within an assembly, so a name any other test
    /// might read — <c>CI</c> above all — would be a race, not a test.
    /// </para>
    /// </remarks>
    [Fact]
    public void ProcessEnvironmentReader_ReadsTheRealEnvironment()
    {
        const string Name = "PREFLIGHT_TEST_ENVIRONMENT_READER_PROBE";
        var reader = new ProcessEnvironmentReader();

        reader.GetVariable(Name).ShouldBeNull();

        try
        {
            Environment.SetEnvironmentVariable(Name, "present");

            reader.GetVariable(Name).ShouldBe("present");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Name, null);
        }
    }
}
