namespace Preflight.Cli.Tests.Reporting;

using Preflight.Abstractions.Model;
using Preflight.Cli.Reporting;
using Preflight.Core;
using Preflight.Core.Execution;
using Preflight.TestSupport;

/// <summary>
/// Fixes the JSON report of <c>--format json</c>, shaped after the <c>run</c>
/// event of the history format.
/// </summary>
/// <remarks>
/// A golden here for the same reason as the console one: the consumer is a
/// parser, and a parser breaks on a renamed property, a reordered array or an
/// enum that arrives as a number — none of which a containment assertion sees.
/// </remarks>
public sealed class JsonReporterTests
{
    private static string Render(RunResult result)
    {
        var output = new StringWriter();

        new JsonReporter(output).Report(result);

        return output.ToString();
    }

    /// <summary>
    /// A cached execution says so in the machine-readable output too.
    /// </summary>
    /// <remarks>
    /// The three goldens beside this one all pin <c>"fromCache": false</c>,
    /// which is a value that could not have been anything else before the
    /// incremental cache existed. Nothing in the repository pinned the true
    /// side until now, and a pipeline that branches on the flag needs it to be
    /// there.
    /// </remarks>
    [Fact]
    public Task Report_WithACachedExecution_WritesFromCacheTrue() =>
        Verify(Render(RunResultFixture.With(
            RunResultFixture.Execution("core.build.compile-probe", RuleStatus.Passed, 0) with
            {
                FromCache = true,
            })));

    [Fact]
    public Task Report_ForTheCanonicalExample_MatchesTheGolden() =>
        Verify(Render(RunResultFixture.CanonicalExample()));

    /// <summary>
    /// Enums are names, never ordinals.
    /// </summary>
    /// <remarks>
    /// <c>"verdict": 2</c> forces every consumer to keep a copy of the enum's
    /// declaration order, and inserting a value into that enum would silently
    /// change the meaning of every record already written — including the
    /// history the instrumentation says has to stay auditable months later.
    /// </remarks>
    [Fact]
    public void Report_WritesEnumsAsNames()
    {
        var rendered = Render(RunResultFixture.CanonicalExample());

        rendered.ShouldContain("\"verdict\": \"Blocked\"");
        rendered.ShouldContain("\"stage\": \"BuildReadiness\"");
        rendered.ShouldContain("\"status\": \"Passed\"");
        rendered.ShouldContain("\"skipReason\": \"DependencyFailed\"");
    }

    /// <remarks>
    /// The determinism guarantee fixes the order and the console report spends
    /// it on the root cause being read before the symptom. A serialiser that
    /// sorted by name for tidiness would take that back, and the console and
    /// the JSON would then disagree about the same run.
    /// </remarks>
    [Fact]
    public void Report_PreservesTheOrderOfExecutions()
    {
        var rendered = Render(RunResultFixture.CanonicalExample());

        var toolchain = rendered.IndexOf("core.workspace.toolchain", StringComparison.Ordinal);
        var configuration = rendered.IndexOf("core.build.configuration", StringComparison.Ordinal);
        var probe = rendered.IndexOf("core.build.compile-probe", StringComparison.Ordinal);

        toolchain.ShouldBeLessThan(configuration);
        configuration.ShouldBeLessThan(probe);
    }

    /// <remarks>
    /// the finding order within a rule is the order the rule produced them, and
    /// the reporter preserves rather than decides it.
    /// </remarks>
    [Fact]
    public Task Report_PreservesFindingOrderWithinARule() =>
        Verify(Render(RunResultFixture.With(
            RunResultFixture.Execution("core.build.compile-probe", RuleStatus.Failed, 0.5) with
            {
                Findings =
                [
                    new Finding { Message = "third to read, first produced" },
                    new Finding { Message = "second" },
                    new Finding { Message = "last produced" },
                ],
            })));

    /// <remarks>The empty-run case, in the form a pipeline can branch on.</remarks>
    [Fact]
    public void Report_WithNothingExecuted_SaysSoAsANumber()
    {
        Render(RunResultFixture.CanonicalExample() with { Executions = [] })
            .ShouldContain("\"executedCount\": 0");
    }

    /// <remarks>
    /// Absent rather than null or empty. A consumer that has to distinguish "no
    /// findings" from "findings I could not read" is a consumer writing a null
    /// check for a case that never happens; omitting the member says the same
    /// thing in less.
    /// </remarks>
    [Fact]
    public void Report_OmitsEmptyCollectionsAndNulls()
    {
        var rendered = Render(RunResultFixture.With(
            RunResultFixture.Execution("core.workspace.toolchain", RuleStatus.Passed, 0.4)));

        rendered.ShouldNotContain("findings");
        rendered.ShouldNotContain("skippedBecauseOf");
        rendered.ShouldNotContain("errorDetail");
    }

    [Fact]
    public Task Report_WithAFullyPopulatedFinding_WritesEveryMember() =>
        Verify(Render(RunResultFixture.With(
            RunResultFixture.Execution("core.build.compile-probe", RuleStatus.Failed, 0.5) with
            {
                Findings =
                [
                    new Finding
                    {
                        Message = "error CS1002: ; expected",
                        Location = new FindingLocation("src/Game/Player.cs", 42, 13),
                        Expected = "a semicolon",
                        Actual = "a newline",
                        Remediation = "add the semicolon",
                    },
                ],
            })));

    /// <remarks>
    /// The same guard the console reporter carries, for the same reason: this
    /// exists to catch a reporter that emits in completion order or walks a
    /// hash set. An intermittent failure here is the defect, not flakiness, and
    /// must not be resolved by deleting the comparison.
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
    /// polluted output applies doubly here: anything else the tool has to say
    /// goes to stderr, or the jq at the end of the pipeline breaks on the first
    /// log line.
    /// </remarks>
    [Fact]
    public void Report_WritesASingleParseableDocument()
    {
        var rendered = Render(RunResultFixture.CanonicalExample());

        Should.NotThrow(() => System.Text.Json.JsonDocument.Parse(rendered));
    }
}
