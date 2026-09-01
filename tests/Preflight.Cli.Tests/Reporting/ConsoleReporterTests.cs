namespace Preflight.Cli.Tests.Reporting;

using System.Globalization;
using System.Text;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Cli.Reporting;
using Preflight.Core;
using Preflight.TestSupport;

/// <summary>
/// Fixes the console report.
/// </summary>
/// <remarks>
/// Golden files rather than <c>ShouldContain</c>, because the correctness of
/// this component is entirely about exact bytes — glyph, alignment, ordering,
/// and which of the two variants was chosen. A containment assertion is
/// invariant under every one of those defects.
/// </remarks>
public sealed class ConsoleReporterTests
{
    private static readonly LocalOverlayDecision NoOverlay =
        new(Applied: false, CiVariable: null, LocalOverlaySuppression.FileAbsent);

    /// <summary>
    /// A pipeline the user asked for by name, which is what every golden
    /// written before ADR-029 describes.
    /// </summary>
    private static readonly PipelineSelection AskedFor =
        new("atlas", PipelineSource.CommandLine);

    private static string Render(
        RunResult result,
        GlyphSet? glyphs = null,
        LocalOverlayDecision? overlay = null,
        bool isInteractive = false,
        Encoding? encoding = null,
        PipelineSelection? selection = null,
        InstalledPipeline? package = null)
    {
        var output = new StringWriter();
        var capabilities = new ConsoleCapabilities(
            output,
            encoding ?? Encoding.UTF8,
            isInteractive,
            IsInputInteractive: false,
            ConsoleCapabilities.DefaultWidth);

        new ConsoleReporter(capabilities, glyphs ?? GlyphSet.Unicode)
            .Report(result, overlay ?? NoOverlay, selection ?? AskedFor, package);

        return output.ToString();
    }

    /// <summary>
    /// A resolved package, named and versioned.
    /// </summary>
    private static InstalledPipeline Resolved(PipelineVersionSource source)
    {
        PackageVersion.TryParse("1.4.0", out var version).ShouldBeTrue();

        return new InstalledPipeline(
            "atlas",
            version!,
            new DirectoryInfo(Path.Combine(Path.GetTempPath(), "pf", "atlas", "1.4.0")),
            source);
    }

    [Fact]
    public Task Report_ForTheDocumentedExample_MatchesTheGolden() =>
        Verify(Render(RunResultFixture.DocumentedExample()));

    /// <remarks>
    /// The regression that matters most in this phase. A run that never met a
    /// package has nothing new to say, and every golden written before packages
    /// existed depends on this line staying silent — if the version leaked into
    /// it, the natural repair would be to re-accept eighteen golden files at
    /// once, leaving a green suite over a broken contract.
    /// </remarks>
    [Fact]
    public void Report_WithoutAPackage_LeavesTheHeaderExactlyAsItWas() =>
        Render(RunResultFixture.DocumentedExample())
            .ShouldContain(" atlas ");

    [Fact]
    public Task Report_WithAPackageFromTheCheckoutRange_SaysNameAtVersionAndWhy() =>
        Verify(Render(
            RunResultFixture.DocumentedExample() with { PipelineVersion = "1.4.0" },
            package: Resolved(PipelineVersionSource.Requirement)));

    [Fact]
    public Task Report_WithAPinnedPackage_SaysPinned() =>
        Verify(Render(
            RunResultFixture.DocumentedExample() with { PipelineVersion = "1.4.0" },
            package: Resolved(PipelineVersionSource.Pin)));

    [Fact]
    public Task Report_WithTheAsciiVariant_MatchesTheGolden() =>
        Verify(Render(RunResultFixture.DocumentedExample(), GlyphSet.Ascii));

    /// <summary>
    /// A result that did not come from this run says so.
    /// </summary>
    /// <remarks>
    /// The cache key makes this the condition on which the whole cache is
    /// acceptable, and it is a golden rather than a containment check because
    /// the placement matters: the marker follows the duration, so a reader
    /// scanning the duration column sees immediately that the 0.0s beside it is
    /// not a rule that got faster. A report that claims a check ran when it did
    /// not is the one thing an accelerated tool must never do.
    /// </remarks>
    [Fact]
    public Task Report_WithACachedExecution_SaysSo() =>
        Verify(Render(RunResultFixture.With(
            RunResultFixture.Execution("core.workspace.toolchain", RuleStatus.Passed, 0.4),
            RunResultFixture.Execution("core.build.compile-probe", RuleStatus.Passed, 0) with
            {
                FromCache = true,
            })));

    /// <summary>
    /// The ASCII variant contains no character outside ASCII. Any of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Swapping the status glyphs and keeping the punctuation is half a fix,
    /// and the half that is missing is invisible in a golden file read on a
    /// UTF-8 terminal. The header separator and the policy-chain arrow are
    /// outside ASCII too, so on the very build agents that default to a codepage
    /// which cannot carry them, a report with correct glyphs still
    /// prints its first two lines as question marks.
    /// </para>
    /// <para>
    /// Asserted as a property of the whole output rather than character by
    /// character, so a decorative character added later cannot slip past by
    /// being one nobody thought to list.
    /// </para>
    /// </remarks>
    [Fact]
    public void Report_WithTheAsciiVariant_ContainsNothingOutsideAscii()
    {
        var rendered = Render(RunResultFixture.DocumentedExample(), GlyphSet.Ascii);

        var offenders = rendered.Where(character => character > 127).Distinct().ToArray();

        offenders.ShouldBeEmpty(
            "The ASCII variant exists for consoles that cannot render these: " +
            string.Join(", ", offenders.Select(character => $"U+{(int)character:X4}")));
    }

    /// <remarks>
    /// The local-overlay rule requires the header to announce an applied local overlay,
    /// and calls trusting nobody to forget the kind of thing that works until
    /// gold week.
    /// </remarks>
    [Fact]
    public Task Report_WithTheLocalOverlayApplied_SaysSoInTheHeader() =>
        Verify(Render(
            RunResultFixture.DocumentedExample(),
            overlay: new LocalOverlayDecision(true, null, LocalOverlaySuppression.None)));

    [Fact]
    public Task Report_InsideCi_NamesTheDetectedVariable() =>
        Verify(Render(
            RunResultFixture.DocumentedExample(),
            overlay: new LocalOverlayDecision(false, "TEAMCITY_VERSION", LocalOverlaySuppression.CiDetected)));

    /// <remarks>
    /// The built-in rule set makes this a correctness claim rather than a cosmetic one: a
    /// commit touching only <c>.md</c> files makes a pre-submit rule report
    /// <c>n/a</c>, never a tick, because saying it passed would claim more than
    /// is known.
    /// </remarks>
    [Fact]
    public Task Report_WithNotApplicable_UsesItsOwnGlyphAndCount() =>
        Verify(Render(RunResultFixture.With(
            RunResultFixture.Execution("core.presubmit.large-file", RuleStatus.NotApplicable, 0.1),
            RunResultFixture.Execution("core.presubmit.forbidden-paths", RuleStatus.NotApplicable, 0.1))));

    /// <remarks>
    /// The console report: "disabled by policy" and "failed, gating" are completely
    /// different situations for whoever is reading, and the line has to say
    /// which one happened.
    /// </remarks>
    [Fact]
    public Task Report_WithASkipCausedByPolicy_SaysDisabledByPolicy() =>
        Verify(Render(RunResultFixture.With(
            RunResultFixture.Execution("core.build.compile-probe", RuleStatus.Skipped, 0) with
            {
                SkippedBecauseOf = [new RuleId("core.build.configuration")],
                SkipReason = SkipReason.DependencyDisabled,
            })));

    /// <remarks>
    /// <c>SkippedBecauseOf</c> is ordered by topological level so the most
    /// likely root comes first. Printing only the first element throws that
    /// ordering away through the formatter; the ids here are chosen so that
    /// alphabetical order and the given order disagree, which is what makes a
    /// silent re-sort visible.
    /// </remarks>
    [Fact]
    public Task Report_WithTwoTerminalAncestors_PrintsBothInTheGivenOrder() =>
        Verify(Render(RunResultFixture.With(
            RunResultFixture.Execution("core.build.compile-probe", RuleStatus.Skipped, 0) with
            {
                SkippedBecauseOf =
                [
                    new RuleId("z.z.root"),
                    new RuleId("a.b.deep"),
                ],
                SkipReason = SkipReason.DependencyFailed,
            })));

    /// <remarks>
    /// Four independently optional members means sixteen legal shapes, and
    /// the console report draws only the one with all of them. A finding carrying
    /// nothing but a message is legal, and is what a rule written in a hurry
    /// produces.
    /// </remarks>
    [Fact]
    public Task Report_RendersEveryShapeOfFinding() =>
        Verify(Render(RunResultFixture.With(
            RunResultFixture.Execution("core.build.configuration", RuleStatus.Failed, 0.2) with
            {
                Findings = AllFindingShapes(),
            })));

    [Fact]
    public Task Report_WithAnErroredRule_ShowsItsOwnGlyphAndDetail() =>
        Verify(Render(RunResultFixture.With(
            RunResultFixture.Execution("core.build.compile-probe", RuleStatus.Errored, 0.3) with
            {
                ErrorDetail = "System.TimeoutException: the probe did not finish within 60s.",
            })));

    /// <remarks>
    /// The third skip reason, and the one that is easiest to leave unwritten
    /// because the happy path never produces it. The console report says the three read
    /// as completely different situations, and "errored" specifically tells the
    /// reader the dependency did not report on the workspace at all — it broke.
    /// </remarks>
    [Fact]
    public Task Report_WithASkipCausedByAnErroredDependency_SaysErroredGating() =>
        Verify(Render(RunResultFixture.With(
            RunResultFixture.Execution("core.build.compile-probe", RuleStatus.Skipped, 0) with
            {
                SkippedBecauseOf = [new RuleId("core.build.configuration")],
                SkipReason = SkipReason.DependencyErrored,
            })));

    /// <summary>
    /// Causes without a reason still render, without a dangling parenthesis.
    /// </summary>
    /// <remarks>
    /// <c>SkipReason</c> is nullable while <c>SkippedBecauseOf</c> is not, so
    /// the shape is legal even though the engine never produces it. The failure
    /// this guards is cosmetic and permanent: a formatter that assumed a reason
    /// was always present would print <c>blocked by  x   ()</c> forever, and
    /// nobody would trace it back to a null on a record.
    /// </remarks>
    [Fact]
    public void Report_WithCausesButNoSkipReason_OmitsTheReasonRatherThanPrintingAnEmptyOne()
    {
        var rendered = Render(RunResultFixture.With(
            RunResultFixture.Execution("core.build.compile-probe", RuleStatus.Skipped, 0) with
            {
                SkippedBecauseOf = [new RuleId("core.build.configuration")],
                SkipReason = null,
            }));

        rendered.ShouldContain("blocked by  core.build.configuration");
        rendered.ShouldNotContain("()");
    }

    /// <remarks>
    /// The local-overlay rule lists <c>--no-local</c> as its own row, and the header says
    /// which of the reasons applied. "Not applied" alone would leave a reader
    /// unable to tell a deliberate flag from a missing file.
    /// </remarks>
    [Fact]
    public Task Report_WithTheOverlayDisabledByFlag_SaysSoRatherThanJustNotApplied() =>
        Verify(Render(
            RunResultFixture.DocumentedExample(),
            overlay: new LocalOverlayDecision(false, null, LocalOverlaySuppression.ExplicitlyDisabled)));

    /// <remarks>
    /// Every status gets a colour, and only a run containing all six exercises
    /// them. Written as one interactive render rather than six, because the
    /// assertion is that each glyph is wrapped — not what any particular code is.
    /// </remarks>
    [Fact]
    public void Report_WhenInteractive_ColoursEveryStatus()
    {
        var rendered = Render(
            RunResultFixture.With(
                RunResultFixture.Execution("core.a.passed", RuleStatus.Passed, 0.1),
                RunResultFixture.Execution("core.a.warning", RuleStatus.Warning, 0.1),
                RunResultFixture.Execution("core.a.failed", RuleStatus.Failed, 0.1),
                RunResultFixture.Execution("core.a.skipped", RuleStatus.Skipped, 0),
                RunResultFixture.Execution("core.a.not-applicable", RuleStatus.NotApplicable, 0.1),
                RunResultFixture.Execution("core.a.errored", RuleStatus.Errored, 0.1)),
            isInteractive: true);

        // Six glyphs, each opened and closed.
        rendered.Split("\u001b[0m").Length.ShouldBe(7);
    }

    [Theory]
    [InlineData(RunVerdict.Passed, "Passed")]
    [InlineData(RunVerdict.PassedWithWarnings, "Passed with warnings")]
    [InlineData(RunVerdict.Blocked, "Blocked")]
    [InlineData(RunVerdict.Errored, "Errored")]
    public void Report_NamesTheVerdictInTheSummary(RunVerdict verdict, string expected)
    {
        Render(RunResultFixture.DocumentedExample() with { Verdict = verdict })
            .ShouldContain("  " + expected + " ");
    }

    /// <remarks>
    /// Guards the arm that fires only if a fifth verdict is added without
    /// visiting the reporter. Rendering an unknown verdict as an empty string
    /// would produce a summary line that reads as success.
    /// </remarks>
    [Fact]
    public void Report_WithAVerdictOutsideTheEnum_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            Render(RunResultFixture.DocumentedExample() with { Verdict = (RunVerdict)99 }));
    }

    /// <remarks>The line has to be greppable.</remarks>
    [Fact]
    public Task Report_WithNothingExecuted_SaysSoRatherThanReadingAsAnOrdinarySuccess() =>
        Verify(Render(RunResultFixture.DocumentedExample() with
        {
            Executions = [],
            Verdict = RunVerdict.Passed,
        }));

    /// <remarks>
    /// <c>--no-skip</c> can turn a green run red. A reader who
    /// cannot see the flag cannot explain the report.
    /// </remarks>
    [Fact]
    public Task Report_WithNoSkipInEffect_SaysSoInTheHeader() =>
        Verify(Render(RunResultFixture.DocumentedExample() with { NoSkip = true }));

    [Fact]
    public Task Report_WithFailOnWarningInEffect_SaysSoInTheHeader() =>
        Verify(Render(RunResultFixture.DocumentedExample() with { FailOnWarning = true }));

    [Fact]
    public Task Report_WithNoPipelineAndNoPolicyFiles_SaysDefaultsOnly() =>
        Verify(Render(RunResultFixture.DocumentedExample() with
        {
            Pipeline = null,
            PolicyChain = [],
        }));

    /// <summary>
    /// A pipeline nobody asked for says where it came from.
    /// </summary>
    /// <remarks>
    /// The same argument the local overlay makes in <c>Docs/design.md 6.3</c>,
    /// one layer up: a run configured by a file nobody passed must not read the
    /// same as one that was asked for. Only this source is annotated — a flag
    /// the user typed needs no explanation, which is why every other golden
    /// here is unchanged. See ADR-029.
    /// </remarks>
    [Fact]
    public Task Report_WithAPipelineSelectedFromTheCheckout_SaysWhereItCameFrom() =>
        Verify(Render(
            RunResultFixture.DocumentedExample(),
            selection: new PipelineSelection("atlas", PipelineSource.Checkout)));

    [Fact]
    public Task Report_WithALocatedFinding_RendersLineAndColumn() =>
        Verify(Render(RunResultFixture.With(
            RunResultFixture.Execution("core.build.compile-probe", RuleStatus.Failed, 0.5) with
            {
                Findings =
                [
                    new Finding
                    {
                        Message = "error CS1002: ; expected",
                        Location = new FindingLocation("src/Game/Player.cs", 42),
                    },
                    new Finding
                    {
                        Message = "error CS0103: the name 'foo' does not exist",
                        Location = new FindingLocation("src/Game/Player.cs", 87, 13),
                    },
                ],
            })));

    /// <remarks>
    /// The console report: no colour outside an interactive terminal, so a CI log is not
    /// polluted with sequences nothing will render.
    /// </remarks>
    [Fact]
    public void Report_WhenOutputIsRedirected_EmitsNoAnsiEscapes()
    {
        Render(RunResultFixture.DocumentedExample(), isInteractive: false)
            .ShouldNotContain("\u001b[");
    }

    /// <remarks>
    /// The other half, and the half a test host can never reach by accident:
    /// under any runner <c>IsOutputRedirected</c> is permanently true, so
    /// without this a reporter that never coloured anything would satisfy the
    /// test above and look correct.
    /// </remarks>
    [Fact]
    public void Report_WhenOutputIsInteractive_EmitsAnsiEscapes()
    {
        Render(RunResultFixture.DocumentedExample(), isInteractive: true)
            .ShouldContain("\u001b[");
    }

    /// <summary>
    /// Durations are formatted with a dot, and cannot be otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The obvious test — render under pt-BR, render under en-US, compare —
    /// cannot be written, and finding out why is worth more than the test would
    /// have been. <c>Directory.Build.props</c> sets
    /// <c>InvariantGlobalization</c> for every project including
    /// <c>Preflight.Cli</c>, so constructing a named culture throws, and the
    /// shipped binary has no ambient culture to be wrong about in the first
    /// place.
    /// </para>
    /// <para>
    /// The <c>InvariantCulture</c> arguments in the reporter are therefore
    /// belt-and-braces rather than load-bearing today. They stay, because the
    /// setting is one line in a props file and the failure it would unmask —
    /// <c>0,4s</c> on a pt-BR machine, <c>0.4s</c> on CI, the determinism guarantee's
    /// byte-identical guarantee holding on each machine separately and failing
    /// between them — is the kind that reads as someone else's environment
    /// being broken.
    /// </para>
    /// <para>
    /// What is assertable is the observable: a dot, in the output, always.
    /// </para>
    /// </remarks>
    [Fact]
    public void Report_FormatsDurationsWithADotDecimalSeparator()
    {
        var rendered = Render(RunResultFixture.DocumentedExample());

        rendered.ShouldContain("0.4s");
        rendered.ShouldNotContain("0,4s");
    }

    /// <remarks>
    /// Twenty renders of one result, byte for byte. This guards against a
    /// reporter that emits in completion order or iterates a hash set: an
    /// intermittent failure here is not flakiness, it is the defect the test
    /// exists to catch, and it must not be "fixed" by removing the comparison.
    /// </remarks>
    [Fact]
    public void Report_RepeatedOverTheSameResult_ProducesIdenticalBytes()
    {
        var result = RunResultFixture.DocumentedExample();
        var first = Render(result);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            Render(result).ShouldBe(first);
        }
    }

    [Fact]
    public void ShortPolicyName_StripsTheDirectoryTheExtensionAndThePrefix()
    {
        ConsoleReporter.ShortPolicyName(Path.Combine("C:", "repo", "preflight.atlas.json")).ShouldBe("atlas");
        ConsoleReporter.ShortPolicyName("team.json").ShouldBe("team");
    }

    private static List<Finding> AllFindingShapes()
    {
        var location = new FindingLocation("config/build/win64.json");
        var findings = new List<Finding>();

        for (var shape = 0; shape < 16; shape++)
        {
            findings.Add(new Finding
            {
                Message = $"Finding shape {shape.ToString(CultureInfo.InvariantCulture)}.",
                Location = (shape & 1) == 0 ? null : location,
                Expected = (shape & 2) == 0 ? null : "an entry",
                Actual = (shape & 4) == 0 ? null : "nothing",
                Remediation = (shape & 8) == 0 ? null : "add the entry",
            });
        }

        return findings;
    }
}
