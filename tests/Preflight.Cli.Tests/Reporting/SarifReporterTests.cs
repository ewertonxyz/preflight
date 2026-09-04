namespace Preflight.Cli.Tests.Reporting;

using System.Text.Json;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Cli.Reporting;
using Preflight.Core;
using Preflight.Core.Execution;
using Preflight.TestSupport;

/// <summary>
/// Fixes the SARIF 2.1.0 document of <c>--format sarif</c>.
/// </summary>
/// <remarks>
/// A golden here for the same reason the JSON reporter has one: the consumer is
/// a parser on somebody else's code review screen, and a renamed property, a
/// reordered array or a level that disagrees with a kind are all invisible to a
/// containment assertion. The unit tests beside it name the individual
/// decisions behind the format, so a failure says which one broke rather than
/// only that the document changed.
/// </remarks>
public sealed class SarifReporterTests
{
    private static string Render(RunResult result, IReadOnlyList<RuleDescriptor>? descriptors = null)
    {
        var output = new StringWriter();

        new SarifReporter(output).Report(result, descriptors ?? RuleDescriptorFixture.ForCanonicalExample());

        return output.ToString();
    }

    private static JsonElement Run(string rendered) =>
        JsonDocument.Parse(rendered).RootElement.GetProperty("runs")[0];

    private static IReadOnlyList<JsonElement> Results(string rendered) =>
        [.. Run(rendered).GetProperty("results").EnumerateArray()];

    private static JsonElement ResultFor(string rendered, string ruleId) =>
        Results(rendered).Single(result => result.GetProperty("ruleId").GetString() == ruleId);

    /// <summary>
    /// A rule that errored produces no result at all.
    /// </summary>
    /// <remarks>
    /// The decision of the whole phase, and the one with a cost worth stating:
    /// somebody reading only <c>results</c> cannot see that a rule errored.
    /// That is correct. Verdict aggregation puts <c>Errored</c> first in
    /// aggregation so a defect in the tool is never reported as a problem with
    /// the workspace, and emitting it as a finding would make Preflight accuse
    /// someone else's commit, on their review screen, for a bug of its own.
    /// What tells the truth instead is the invocation:
    /// <c>executionSuccessful</c> false and exit code 3, which the exit-code
    /// contract designed to call the tool's owner.
    ///
    /// The healthy sibling in the same run is what stops this passing against a
    /// reporter that simply emits nothing.
    /// </remarks>
    [Fact]
    public void Report_ForAnErroredRule_WritesNoResultAndAToolExecutionNotificationInstead()
    {
        var rendered = Render(RunResultFixture.With(
            RunResultFixture.Execution("core.build.compile-probe", RuleStatus.Errored, 0.1) with
            {
                ErrorDetail = "boom",
            },
            RunResultFixture.Execution("core.workspace.toolchain", RuleStatus.Failed, 0.4) with
            {
                Findings = [new Finding { Message = "real workspace problem" }],
            }));

        var results = Results(rendered);

        results.Count.ShouldBe(1);
        results[0].GetProperty("ruleId").GetString().ShouldBe("core.workspace.toolchain");
        results
            .Select(result => result.GetProperty("ruleId").GetString())
            .ShouldNotContain("core.build.compile-probe");

        var notifications = Run(rendered)
            .GetProperty("invocations")[0]
            .GetProperty("toolExecutionNotifications")
            .EnumerateArray()
            .ToArray();

        notifications.Length.ShouldBe(1);
        notifications[0].GetProperty("message").GetProperty("text").GetString()!.ShouldContain("boom");
        notifications[0]
            .GetProperty("associatedRule")
            .GetProperty("id")
            .GetString()
            .ShouldBe("core.build.compile-probe");
    }

    /// <summary>
    /// <c>--fail-on-warning</c> changes the invocation and not one level.
    /// </summary>
    /// <remarks>
    /// The promotion is a property of the invocation, not of the finding.
    /// Rewriting the level under the flag would make the same findings produce
    /// two different documents, and a consumer filtering on <c>error</c> would
    /// be reading the command line through the severity field.
    /// </remarks>
    [Fact]
    public void Report_WithFailOnWarningInEffect_LeavesTheLevelAloneAndShowsThePromotionInTheInvocation()
    {
        var warned = RunResultFixture.With(
            RunResultFixture.Execution("core.workspace.toolchain", RuleStatus.Warning, 0.1) with
            {
                EffectiveSeverity = Severity.Warning,
                Findings = [new Finding { Message = "m" }],
            });

        var tolerated = Render(warned with
        {
            FailOnWarning = false,
            Verdict = RunVerdict.PassedWithWarnings,
        });

        var promoted = Render(warned with
        {
            FailOnWarning = true,
            Verdict = RunVerdict.Blocked,
        });

        Results(tolerated)[0].GetProperty("level").GetString().ShouldBe("warning");
        Results(promoted)[0].GetProperty("level").GetString().ShouldBe("warning");

        Invocation(tolerated).GetProperty("exitCode").GetInt32().ShouldBe(0);
        Invocation(tolerated).GetProperty("executionSuccessful").GetBoolean().ShouldBeTrue();

        Invocation(promoted).GetProperty("exitCode").GetInt32().ShouldBe(1);
        Invocation(promoted).GetProperty("executionSuccessful").GetBoolean().ShouldBeFalse();
    }

    private static JsonElement Invocation(string rendered) => Run(rendered).GetProperty("invocations")[0];

    /// <summary>
    /// One driver rule per execution, in presentation order, and every result
    /// points at its own.
    /// </summary>
    /// <remarks>
    /// Emitting only the rules that produced a finding is the tempting
    /// alternative and it breaks <c>ruleIndex</c> between two runs of the same
    /// workspace, which is the opposite of what the determinism guarantee asks
    /// for. A reporter that deduplicated or sorted by name would break the
    /// index without breaking anything a reader would notice.
    /// </remarks>
    [Fact]
    public void Report_WritesOneDriverRulePerExecutionInPresentationOrderAndAStableRuleIndex()
    {
        var rendered = Render(RunResultFixture.CanonicalExample());

        var rules = Run(rendered)
            .GetProperty("tool")
            .GetProperty("driver")
            .GetProperty("rules")
            .EnumerateArray()
            .ToArray();

        rules.Length.ShouldBe(3);
        rules.Select(rule => rule.GetProperty("id").GetString()).ShouldBe(
        [
            "core.workspace.toolchain",
            "core.build.configuration",
            "core.build.compile-probe",
        ]);

        foreach (var result in Results(rendered))
        {
            rules[result.GetProperty("ruleIndex").GetInt32()]
                .GetProperty("id")
                .GetString()
                .ShouldBe(result.GetProperty("ruleId").GetString());
        }
    }

    /// <summary>
    /// The message carries the evidence, in the order the console report fixed.
    /// </summary>
    /// <remarks>
    /// Not <c>fixes[]</c>: that field carries <c>artifactChanges</c>, an
    /// applicable correction, and writing to the
    /// workspace a non-goal — offering the field would promise what the tool
    /// refuses to do. Not <c>properties</c> either, because no SARIF consumer
    /// renders the property bag, and evidence nobody displays is evidence
    /// nobody reads.
    /// </remarks>
    [Fact]
    public void Report_ForAFullyPopulatedFinding_FoldsExpectedActualAndFixIntoTheMessageInThatOrder()
    {
        var rendered = Render(RunResultFixture.CanonicalExample());
        var text = ResultFor(rendered, "core.build.configuration")
            .GetProperty("message")
            .GetProperty("text")
            .GetString()!;

        text.ShouldContain("Missing platform configuration entry.");
        text.IndexOf("a \"contentRoot\" entry", StringComparison.Ordinal)
            .ShouldBeLessThan(text.IndexOf("key not present", StringComparison.Ordinal));
        text.IndexOf("key not present", StringComparison.Ordinal)
            .ShouldBeLessThan(text.IndexOf("add \"contentRoot\"", StringComparison.Ordinal));

        rendered.ShouldNotContain("\"fixes\"");
        rendered.ShouldNotContain("\"properties\"");
    }

    /// <remarks>
    /// Every member below <c>Message</c> is independently optional, which is
    /// what a rule written in a hurry produces. The console reporter has the
    /// same guard for the same reason: a row of empty labels is a template
    /// pretending to be evidence, and here it would be trailing whitespace
    /// inside a JSON string that nothing ever trims.
    /// </remarks>
    [Fact]
    public void Report_ForAFindingWithNothingButAMessage_WritesTheMessageAlone()
    {
        var rendered = Render(RunResultFixture.With(
            RunResultFixture.Execution("core.build.configuration", RuleStatus.Failed, 0.5) with
            {
                Findings = [new Finding { Message = "only this" }],
            }));

        var result = ResultFor(rendered, "core.build.configuration");

        result.GetProperty("message").GetProperty("text").GetString().ShouldBe("only this");
        result.TryGetProperty("locations", out _).ShouldBeFalse();
    }

    /// <summary>
    /// A rule that failed or warned without producing a finding still says what
    /// happened.
    /// </summary>
    /// <remarks>
    /// Not in the phase's manifest, and added because the path is real:
    /// <c>RuleOutcome.Fail()</c> takes no findings, so a rule written in a
    /// hurry produces exactly this. Without it the result would carry an empty
    /// message on a review screen — which is the shape of defect this reporter
    /// exists to avoid, arrived at from the other direction.
    /// </remarks>
    [Theory]
    [InlineData(RuleStatus.Warning, "The rule reported a warning.")]
    [InlineData(RuleStatus.Failed, "The rule failed.")]
    public void Report_ForAFailingRuleWithNoFinding_StillSaysWhatHappened(
        RuleStatus status,
        string expected)
    {
        var rendered = Render(RunResultFixture.With(
            RunResultFixture.Execution("core.build.configuration", status, 0.5)));

        ResultFor(rendered, "core.build.configuration")
            .GetProperty("message")
            .GetProperty("text")
            .GetString()
            .ShouldBe(expected);
    }

    /// <summary>
    /// A rule with no descriptor is written by its id alone.
    /// </summary>
    /// <remarks>
    /// Not in the phase's manifest. The engine only ever reports on rules it
    /// discovered, so this is unreachable through the CLI and reachable through
    /// this reporter's own public surface — which is exactly what a test
    /// exercises. What it pins is that the document degrades rather than
    /// invents: a fabricated display name on somebody's review screen is worse
    /// than a missing one, and throwing would turn a cosmetic gap into a failed
    /// run of the whole tool.
    /// </remarks>
    [Fact]
    public void Report_ForAnExecutionWithNoDescriptor_WritesTheIdAndNoName()
    {
        var rendered = Render(
            RunResultFixture.With(
                RunResultFixture.Execution("core.workspace.toolchain", RuleStatus.Passed, 0.4)),
            []);

        var rule = Run(rendered).GetProperty("tool").GetProperty("driver").GetProperty("rules")[0];

        rule.GetProperty("id").GetString().ShouldBe("core.workspace.toolchain");
        rule.TryGetProperty("name", out _).ShouldBeFalse();
        rule.TryGetProperty("shortDescription", out _).ShouldBeFalse();
        rule.TryGetProperty("helpUri", out _).ShouldBeFalse();
    }

    /// <summary>
    /// An errored rule with no detail still produces a notification that says
    /// something.
    /// </summary>
    /// <remarks>
    /// Not in the phase's manifest. <c>RuleExecution.ErrorDetail</c> is
    /// nullable, and a notification whose <c>message.text</c> was empty would
    /// be the tool reporting a defect of its own without saying what it was —
    /// which is the half of verdict aggregation that makes the exit code
    /// actionable.
    /// </remarks>
    [Fact]
    public void Report_ForAnErroredRuleWithNoDetail_StillWritesANotification()
    {
        var rendered = Render(RunResultFixture.With(
            RunResultFixture.Execution("core.build.compile-probe", RuleStatus.Errored, 0.1)));

        Invocation(rendered)
            .GetProperty("toolExecutionNotifications")[0]
            .GetProperty("message")
            .GetProperty("text")
            .GetString()
            .ShouldNotBeNullOrWhiteSpace();
    }

    /// <remarks>
    /// <c>RuleDescriptor.Documentation</c> is nullable (the rule descriptor),
    /// and an absent <c>helpUri</c> is absent rather than empty: a consumer
    /// rendering "documentation" as a link to nowhere is worse than one that
    /// renders no link.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("https://example.invalid/r")]
    public void Report_WritesHelpUriOnlyWhenTheDescriptorHasDocumentation(string? documentation)
    {
        var rendered = Render(
            RunResultFixture.With(
                RunResultFixture.Execution("core.workspace.toolchain", RuleStatus.Passed, 0.4)),
            [RuleDescriptorFixture.Documented("core.workspace.toolchain", documentation)]);

        var rule = Run(rendered).GetProperty("tool").GetProperty("driver").GetProperty("rules")[0];

        rule.GetProperty("shortDescription").GetProperty("text").GetString()
            .ShouldBe("core.workspace.toolchain");

        if (documentation is null)
        {
            rule.TryGetProperty("helpUri", out _).ShouldBeFalse();
        }
        else
        {
            rule.GetProperty("helpUri").GetString().ShouldBe(documentation);
        }
    }

    /// <summary>
    /// Exactly three fields may differ between two runs of the same workspace,
    /// and a duration is not one of them.
    /// </summary>
    /// <remarks>
    /// The determinism guarantee qualifies its byte-identical guarantee for the
    /// run id and the durations. This reporter does not emit a duration at all,
    /// so its variation is narrower than the other two reporters': the run id
    /// and the two invocation timestamps. <c>partialFingerprints</c> stays out
    /// for the same reason <c>Finding.Rank</c> was refused: a field no consumer
    /// reads is a field that can be wrong for years without anyone noticing. It
    /// is worth adding on the day something reads it, and not before.
    /// </remarks>
    [Fact]
    public void Report_WritesTheRunIdAndTheTimestampsAndNothingElseThatVaries()
    {
        var rendered = Render(RunResultFixture.CanonicalExample());

        Run(rendered)
            .GetProperty("automationDetails")
            .GetProperty("guid")
            .GetString()
            .ShouldBe(RunResultFixture.FixedRunId.ToString());

        Invocation(rendered).TryGetProperty("startTimeUtc", out _).ShouldBeTrue();
        Invocation(rendered).TryGetProperty("endTimeUtc", out _).ShouldBeTrue();

        rendered.ShouldNotContain("durationMs");
        rendered.ShouldNotContain("partialFingerprints");
    }

    /// <summary>
    /// A run that executed nothing writes an empty <c>results</c> array, not no
    /// array.
    /// </summary>
    /// <remarks>
    /// The SARIF reporter is one of the downstream consumers of
    /// <c>RunVerdict</c>, and to a parser an absent array and an empty one are
    /// different facts: the first is a document that forgot to say, the second
    /// is a run that found nothing.
    /// </remarks>
    [Fact]
    public void Report_WithNothingExecuted_WritesAnEmptyResultsArrayRatherThanOmittingIt()
    {
        var rendered = Render(
            RunResultFixture.CanonicalExample() with
            {
                Executions = [],
                Verdict = RunVerdict.Passed,
            },
            []);

        Run(rendered).GetProperty("results").GetArrayLength().ShouldBe(0);
        Invocation(rendered).GetProperty("executionSuccessful").GetBoolean().ShouldBeTrue();
    }

    /// <remarks>
    /// The same guard the other two reporters carry. An intermittent failure
    /// here is the defect — a reporter emitting in completion order, or walking
    /// a hash set — and must not be resolved by deleting the comparison.
    /// </remarks>
    [Fact]
    public void Report_RepeatedOverTheSameResult_ProducesIdenticalBytes()
    {
        var result = RunResultFixture.CanonicalExample();
        var first = Render(result);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            Render(result).ShouldBe(first);
        }
    }

    /// <remarks>
    /// One document, parseable as a whole. The console report's warning about
    /// polluted output applies here as it does to <c>--format json</c>:
    /// everything else the tool has to say goes to standard error, or the tool
    /// at the end of the pipeline breaks on the first log line.
    /// </remarks>
    [Fact]
    public void Report_WritesASingleParseableDocument()
    {
        var rendered = Render(RunResultFixture.CanonicalExample());

        Should.NotThrow(() => JsonDocument.Parse(rendered));

        var root = JsonDocument.Parse(rendered).RootElement;

        root.TryGetProperty("$schema", out _).ShouldBeTrue();
        root.GetProperty("version").GetString().ShouldBe("2.1.0");
    }

    [Fact]
    public Task Report_ForTheCanonicalExample_MatchesTheGolden() =>
        Verify(Render(RunResultFixture.CanonicalExample()));

    /// <summary>
    /// Every status in one document.
    /// </summary>
    /// <remarks>
    /// The backstop for the unit tests above, in the shape a consumer actually
    /// receives: the six statuses, each at a different severity, with the
    /// <c>Errored</c> one diverted to the invocation. Built on the mould of
    /// <c>ConsoleReporterTests.Report_WhenInteractive_ColoursEveryStatus</c>,
    /// which already assembles the six.
    /// </remarks>
    [Fact]
    public Task Report_WithEveryStatusShape_MatchesTheGolden() =>
        Verify(Render(
            RunResultFixture.With(
                RunResultFixture.Execution("core.a.passed", RuleStatus.Passed, 0.1) with
                {
                    EffectiveSeverity = Severity.Information,
                },
                RunResultFixture.Execution("core.b.warning", RuleStatus.Warning, 0.2) with
                {
                    EffectiveSeverity = Severity.Warning,
                    Findings = [new Finding { Message = "a warning finding" }],
                },
                RunResultFixture.Execution("core.c.failed", RuleStatus.Failed, 0.3) with
                {
                    EffectiveSeverity = Severity.Error,
                    Findings =
                    [
                        new Finding
                        {
                            Message = "a failing finding",
                            Location = new FindingLocation("src/Game/Player.cs", 42, 13),
                            Expected = "a semicolon",
                            Actual = "a newline",
                            Remediation = "add the semicolon",
                        },
                    ],
                },
                RunResultFixture.Execution("core.d.skipped", RuleStatus.Skipped, 0) with
                {
                    SkippedBecauseOf = [new RuleId("core.c.failed")],
                    SkipReason = SkipReason.DependencyFailed,
                },
                RunResultFixture.Execution("core.e.not-applicable", RuleStatus.NotApplicable, 0.4),
                RunResultFixture.Execution("core.f.errored", RuleStatus.Errored, 0.5) with
                {
                    ErrorDetail = "System.InvalidOperationException: the rule is broken",
                }) with
            {
                Verdict = RunVerdict.Errored,
            },
            [
                RuleDescriptorFixture.Rule("core.a.passed"),
                RuleDescriptorFixture.Documented("core.b.warning", "https://example.invalid/warning"),
                RuleDescriptorFixture.Rule("core.c.failed"),
                RuleDescriptorFixture.Rule("core.d.skipped"),
                RuleDescriptorFixture.Rule("core.e.not-applicable"),
                RuleDescriptorFixture.Rule("core.f.errored"),
            ]));
}
