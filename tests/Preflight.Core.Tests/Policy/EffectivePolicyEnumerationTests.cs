namespace Preflight.Core.Tests.Policy;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core.Policy;

/// <summary>
/// Fixes the enumeration <c>preflight explain</c> is built on: every effective
/// value for one rule, in a fixed order, including the <c>settings</c> keys
/// whose names nothing in the engine knows in advance.
/// </summary>
/// <remarks>
/// the explain command for the output this feeds, the policy schema for why
/// <c>settings</c> cannot be enumerated from a schema, and the determinism
/// guarantee for why the order is part of the contract rather than an
/// implementation detail.
/// </remarks>
public sealed class EffectivePolicyEnumerationTests
{
    private static readonly RuleDescriptor LargeFile = new()
    {
        Id = new RuleId("core.presubmit.large-file"),
        DisplayName = "Large changed file",
        Stage = ValidationStage.PreSubmit,
    };

    private static IReadOnlyList<EffectivePolicyEntry> EntriesFor(string? productionJson = null) =>
        EffectivePolicy.Build(
                [LargeFile],
                productionJson is null ? null : PolicyDocument.Parse(productionJson, "preflight.atlas.json"),
                local: null,
                setOverrides: [], target: StatedBuildTarget.Unstated)
            .RuleEntries(LargeFile.Id);

    /// <remarks>
    /// The order is the assertion. The explain command prints this table, and
    /// the determinism guarantee makes a printed table diffable — a set that
    /// happens to be right today and reorders when a dictionary rehashes fails
    /// intermittently, on someone else's machine — the worst way a
    /// determinism guarantee can break, because it breaks for somebody else.
    /// </remarks>
    [Fact]
    public void RuleEntries_ReturnsTheDeclaredRuleKeys_InSchemaDeclarationOrder()
    {
        var entries = EntriesFor();

        entries.Select(entry => entry.Key).ShouldBe([
            "enabled",
            "blocking",
            "gating",
            "severity",
            "timeoutSeconds",
        ]);
    }

    /// <remarks>
    /// The reason this member exists. <c>ReaderFor</c> looks a key up by name
    /// and <c>RuleValue</c> throws on a path that is not there, so neither can
    /// answer "which settings keys does this rule actually have".
    /// </remarks>
    [Fact]
    public void RuleEntries_FlattensSettingsIntoDottedKeys()
    {
        var entries = EntriesFor(
            """
            {
              "schemaVersion": 1,
              "rules": {
                "core.presubmit.large-file": {
                  "settings": { "maxBytes": 52428800 }
                }
              }
            }
            """);

        entries.Select(entry => entry.Key).ShouldContain("settings.maxBytes");
        entries.Single(entry => entry.Key == "settings.maxBytes").Value.Value.ShouldBe(52428800L);
    }

    [Fact]
    public void RuleEntries_FlattensNestedSettings_ToTheFullDottedPath()
    {
        var entries = EntriesFor(
            """
            {
              "schemaVersion": 1,
              "rules": {
                "core.presubmit.large-file": {
                  "settings": { "limits": { "maxBytes": 1024 } }
                }
              }
            }
            """);

        entries.Select(entry => entry.Key).ShouldContain("settings.limits.maxBytes");
    }

    /// <remarks>
    /// Ordinal, not by insertion: settings arrive from a JSON object, whose
    /// member order is whatever the author typed and whose dictionary does not
    /// preserve it anyway.
    /// </remarks>
    [Fact]
    public void RuleEntries_OrdersSettingsKeysOrdinally_AfterTheDeclaredKeys()
    {
        var entries = EntriesFor(
            """
            {
              "schemaVersion": 1,
              "rules": {
                "core.presubmit.large-file": {
                  "settings": { "zeta": 1, "alpha": 2, "Mu": 3 }
                }
              }
            }
            """);

        entries.Select(entry => entry.Key).ShouldBe([
            "enabled",
            "blocking",
            "gating",
            "severity",
            "timeoutSeconds",
            "settings.Mu",
            "settings.alpha",
            "settings.zeta",
        ]);
    }

    /// <remarks>
    /// An empty <c>settings</c> contributes no rows at all. The explain command
    /// lists effective values, and an object with nothing in it has none — a
    /// bare <c>settings</c> row with no value would be a header pretending to
    /// be data.
    /// </remarks>
    [Fact]
    public void RuleEntries_WithNoSettings_EmitsNoSettingsRow()
    {
        var entries = EntriesFor();

        entries.Select(entry => entry.Key)
            .ShouldNotContain(key => key.StartsWith("settings", StringComparison.Ordinal));
    }

    /// <remarks>
    /// Each row carries the whole history, not just the winning value, because
    /// the explain command's <c>overrides preflight.base.json:18 (5242880)</c>
    /// line is read from it.
    /// </remarks>
    [Fact]
    public void RuleEntries_CarriesTheProvenanceOfEachValue()
    {
        var entries = EntriesFor(
            """
            {
              "schemaVersion": 1,
              "rules": {
                "core.presubmit.large-file": { "blocking": false }
              }
            }
            """);

        var blocking = entries.Single(entry => entry.Key == "blocking");

        blocking.Value.Origin.ShouldBeOfType<PolicyOrigin.FromFile>();
        blocking.Value.History.ShouldHaveSingleItem().Origin.ShouldBeOfType<PolicyOrigin.DescriptorDefault>();
    }

    /// <remarks>
    /// Empty, not an exception. An unknown id is the caller's question to
    /// answer — with a suggestion, per policy validation — and it can only do
    /// that if it gets a value back.
    /// </remarks>
    [Fact]
    public void RuleEntries_ForAnUnknownRuleId_ReturnsAnEmptyList()
    {
        var policy = EffectivePolicy.Build([LargeFile], pipeline: null, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        policy.RuleEntries(new RuleId("core.presubmit.no-such-rule")).ShouldBeEmpty();
    }
}
