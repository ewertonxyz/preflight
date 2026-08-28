namespace Preflight.Core.Tests.History;

using System.Text.Json;
using Preflight.Abstractions;
using Preflight.Core.History;
using Preflight.TestSupport;

/// <summary>
/// The one thing that makes <c>--format json</c> and the NDJSON history the
/// same document rather than two that drift.
/// </summary>
/// <remarks>
/// <c>JsonReporter</c> promised this in its own remarks for the whole of
/// the CLI — "the thing a pipeline parses today and the thing the history holds
/// in the history are the same shape" — and nothing defended the promise until
/// there were two writers to defend it against.
/// </remarks>
public sealed class RunEventDocumentTests
{
    /// <remarks>
    /// The member is omitted rather than written as null, and the options that
    /// make that so are load-bearing: emitting an explicit null would change the
    /// bytes of every existing golden file and of the byte-identity guarantee
    /// that `run --format json` makes, for a run that never met a package.
    /// </remarks>
    [Fact]
    public void For_WithoutAPipelineVersion_OmitsTheMember() =>
        JsonSerializer.Serialize(
            RunEventDocument.For(RunResultFixture.DocumentedExample()), RunEventDocument.SingleLine)
            .ShouldNotContain("pipelineVersion");

    [Fact]
    public void For_WithAPipelineVersion_WritesIt() =>
        JsonSerializer.Serialize(
            RunEventDocument.For(RunResultFixture.DocumentedExample() with
            {
                PipelineVersion = "1.4.0",
            }),
            RunEventDocument.SingleLine)
            .ShouldContain("\"pipelineVersion\":\"1.4.0\"");

    /// <remarks>
    /// Provenance is the last thing worth dropping. The noisiest runs are
    /// exactly the ones somebody comes back to, and a record that cannot say
    /// which package produced it is a record about nothing in particular. See
    /// ADR-034.
    /// </remarks>
    [Fact]
    public void Truncated_KeepsThePipelineAndItsVersion()
    {
        var json = JsonSerializer.Serialize(
            RunEventDocument.Truncated(RunResultFixture.DocumentedExample() with
            {
                PipelineVersion = "1.4.0",
            }),
            RunEventDocument.SingleLine);

        json.ShouldContain("\"pipelineVersion\":\"1.4.0\"");
        json.ShouldContain("\"pipeline\"");
    }

    [Fact]
    public void Indented_AndSingleLine_DifferOnlyInIndentation()
    {
        var run = RunResultFixture.DocumentedExample();

        var single = JsonSerializer.Serialize(RunEventDocument.For(run), RunEventDocument.SingleLine);
        var indented = JsonSerializer.Serialize(RunEventDocument.For(run), RunEventDocument.Indented);

        single.ShouldNotContain("\n");
        indented.ShouldContain("\n");

        // Both sides go through the same round-trip, so the comparison is about
        // the members and their order rather than about how a JsonElement is
        // re-serialised.
        Normalise(indented).ShouldBe(Normalise(single));
    }

    [Fact]
    public void SingleLine_IsNotIndented()
    {
        RunEventDocument.SingleLine.WriteIndented.ShouldBeFalse();
        RunEventDocument.Indented.WriteIndented.ShouldBeTrue();
    }

    /// <remarks>
    /// An ordinal would make every record already on disk mean something else
    /// the first time a value was inserted into the enum.
    /// </remarks>
    [Fact]
    public void For_WritesEnumsByName()
    {
        var json = JsonSerializer.Serialize(
            RunEventDocument.For(RunResultFixture.DocumentedExample()),
            RunEventDocument.SingleLine);

        json.ShouldContain("\"verdict\":\"Blocked\"");
        json.ShouldContain("\"stage\":\"BuildReadiness\"");
        json.ShouldContain("\"status\":\"Passed\"");
    }

    /// <summary>
    /// The truncated record of the history format keeps <c>fromCache</c>.
    /// </summary>
    /// <remarks>
    /// The finding detail is what fills 64 KB and what the summary drops.
    /// <c>fromCache</c> costs five bytes and decides whether the duration beside
    /// it is a run or a lookup, so a summary without it would contribute a
    /// nought-second execution to the report's ranking with nothing to mark
    /// it.
    /// </remarks>
    [Fact]
    public void Truncated_KeepsFromCache()
    {
        var run = RunResultFixture.With(
            RunResultFixture.Execution("core.build.compile-probe", RuleStatus.Passed, 0) with
            {
                FromCache = true,
            });

        var json = JsonSerializer.Serialize(RunEventDocument.Truncated(run), RunEventDocument.SingleLine);

        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("executions")[0]
            .GetProperty("fromCache").GetBoolean().ShouldBeTrue();
    }

    /// <remarks>
    /// Re-serialising the parsed document with the single-line options is the
    /// comparison that matters: it proves the two carry the same members in the
    /// same order, which byte-comparing formatted text cannot.
    /// </remarks>
    private static string Normalise(string json)
    {
        using var document = JsonDocument.Parse(json);

        return JsonSerializer.Serialize(document.RootElement, RunEventDocument.SingleLine);
    }
}
