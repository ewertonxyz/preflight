namespace Preflight.Core.Tests.Caching;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core.Caching;

/// <summary>
/// What a cached result survives being written down as.
/// </summary>
public sealed class CachedOutcomeDocumentTests
{
    /// <remarks>
    /// Every member of a finding, because the round trip is the whole contract:
    /// a cached failure that lost its remediation would put a report in front of
    /// somebody with half the evidence the first run had, and the console report calls a
    /// failure without a fix half the work.
    /// </remarks>
    [Fact]
    public void RoundTrip_KeepsEveryMemberOfAFullyPopulatedFinding()
    {
        var outcome = RuleOutcome.Failed(new Finding
        {
            Message = "Changed file exceeds the configured size limit.",
            Location = new FindingLocation("content/textures/atlas.png", 12, 4),
            Expected = "at most 5 MB",
            Actual = "7.4 MB",
            Remediation = "store it in the content pipeline instead",
        });

        var restored = CachedOutcomeDocument
            .Deserialise(CachedOutcomeDocument.Serialise(outcome))
            .ShouldNotBeNull();

        restored.Status.ShouldBe(RuleStatus.Failed);
        restored.Findings.ShouldHaveSingleItem().ShouldBe(outcome.Findings[0]);
    }

    [Theory]
    [InlineData(RuleStatus.Passed)]
    [InlineData(RuleStatus.Warning)]
    [InlineData(RuleStatus.Failed)]
    [InlineData(RuleStatus.NotApplicable)]
    public void RoundTrip_KeepsEveryStatusTheCacheStores(RuleStatus status)
    {
        var restored = CachedOutcomeDocument
            .Deserialise(CachedOutcomeDocument.Serialise(new RuleOutcome { Status = status }))
            .ShouldNotBeNull();

        restored.Status.ShouldBe(status);
        restored.Findings.ShouldBeEmpty();
    }

    /// <remarks>
    /// By name, not by ordinal. The consumer here is this same program a week
    /// later, so an ordinal would make inserting a value into the enum silently
    /// change what every entry already on disk means.
    /// </remarks>
    [Fact]
    public void Serialise_WritesTheStatusByName() =>
        CachedOutcomeDocument.Serialise(RuleOutcome.NotApplicable())
            .ShouldContain("\"status\":\"NotApplicable\"");

    /// <remarks>
    /// A cache entry that cannot be read is a miss, never an error. It is a file
    /// this program wrote for its own convenience, and refusing to validate a
    /// workspace because of one would be the optimisation deciding it outranks
    /// the thing it optimises.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    [InlineData("""{"status":"Sideways"}""")]
    public void Deserialise_ForAnythingUnusable_IsNull(string content) =>
        CachedOutcomeDocument.Deserialise(content).ShouldBeNull();

    /// <remarks>
    /// An entry with no findings array is not damage: a passing rule has none,
    /// and the serialiser omits what is null.
    /// </remarks>
    [Fact]
    public void Deserialise_ForAnEntryWithoutFindings_IsAnEmptyList() =>
        CachedOutcomeDocument.Deserialise("""{"status":"Passed"}""")
            .ShouldNotBeNull()
            .Findings.ShouldBeEmpty();
}
