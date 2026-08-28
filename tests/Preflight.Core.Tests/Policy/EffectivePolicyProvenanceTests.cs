namespace Preflight.Core.Tests.Policy;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core.Policy;

/// <summary>
/// Fixes provenance: every effective value carries where it came from and its
/// full override history, which is what lets the CLI's <c>preflight explain</c>
/// exist without reworking the merge.
/// </summary>
/// <remarks>policy validation and 13.1.</remarks>
public sealed class EffectivePolicyProvenanceTests
{
    private static readonly RuleDescriptor LargeFile = new()
    {
        Id = new RuleId("core.presubmit.large-file"),
        DisplayName = "Large file",
        Stage = ValidationStage.PreSubmit,
        DefaultGating = true,
    };

    [Fact]
    public void Build_ForAnUntouchedRuleField_OriginIsRuleDescriptorDefault()
    {
        var policy = EffectivePolicy.Build([LargeFile], pipeline: null, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        policy.RuleValue<bool>(LargeFile.Id, "gating").Origin.ShouldBeOfType<PolicyOrigin.DescriptorDefault>();
    }

    [Fact]
    public void Build_ForAnUntouchedRootKey_OriginIsEngineDefault_DistinctFromRuleDescriptorDefault()
    {
        var policy = EffectivePolicy.Build([LargeFile], pipeline: null, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        policy.RootValue<long>("maxDegreeOfParallelism").Origin.ShouldBeOfType<PolicyOrigin.EngineDefault>();
    }

    [Fact]
    public void Build_ForAnUntouchedRootKey_HistoryIsEmpty()
    {
        var policy = EffectivePolicy.Build([LargeFile], pipeline: null, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        policy.RootValue<long>("maxDegreeOfParallelism").History.ShouldBeEmpty();
    }

    [Fact]
    public void Build_ForAValueOverriddenOnce_HistoryContainsExactlyTheOriginalAndTheOverride()
    {
        var production = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "blocking": false } } }
            """, "atlas.json");

        var policy = EffectivePolicy.Build([LargeFile], production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);
        var value = policy.RuleValue<bool>(LargeFile.Id, "blocking");

        value.History.Count.ShouldBe(1);
        value.History[0].Origin.ShouldBeOfType<PolicyOrigin.DescriptorDefault>();
        value.Origin.ShouldBeOfType<PolicyOrigin.FromFile>();
        value.Value.ShouldBeFalse();
    }

    [Fact]
    public void Build_ForAValueOverriddenAtEveryLayer_HistoryListsAllFourInOrderOldestToNewest()
    {
        var production = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "blocking": false } } }
            """, "atlas.json");
        var local = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "blocking": true } } }
            """, "local.json");
        var setOverrides = new[]
        {
            new PolicySetOverride { RuleId = LargeFile.Id, Path = "blocking", TypedValue = false },
        };

        var policy = EffectivePolicy.Build([LargeFile], production, local, setOverrides, StatedBuildTarget.Unstated);
        var value = policy.RuleValue<bool>(LargeFile.Id, "blocking");

        value.History.Count.ShouldBe(3);
        value.History[0].Origin.ShouldBeOfType<PolicyOrigin.DescriptorDefault>();
        value.History[1].Origin.ShouldBeOfType<PolicyOrigin.FromFile>();
        value.History[2].Origin.ShouldBeOfType<PolicyOrigin.FromFile>();
        value.Origin.ShouldBeOfType<PolicyOrigin.FromCommandLine>();
        value.Value.ShouldBeFalse();
    }

    [Fact]
    public void Build_MirrorsTheDesignDocExplainExample_ForTheDocumentedFourFieldScenario()
    {
        var descriptor = LargeFile with { DefaultSeverity = Severity.Error, DefaultTimeoutSeconds = 60 };
        var production = PolicyDocument.Parse("""
            {
              "schemaVersion": 1,
              "rules": {
                "core.presubmit.large-file": {
                  "enabled": true,
                  "blocking": true,
                  "severity": "error",
                  "settings": { "maxBytes": 5242880 }
                }
              }
            }
            """, "preflight.base.json");
        var local = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 52428800 } } } }
            """, "preflight.atlas.json");

        var policy = EffectivePolicy.Build([descriptor], production, local, setOverrides: [], target: StatedBuildTarget.Unstated);

        policy.RuleValue<bool>(descriptor.Id, "enabled").Value.ShouldBeTrue();
        policy.RuleValue<bool>(descriptor.Id, "blocking").Value.ShouldBeTrue();
        policy.RuleValue<bool>(descriptor.Id, "gating").Origin.ShouldBeOfType<PolicyOrigin.DescriptorDefault>();
        policy.RuleValue<Severity>(descriptor.Id, "severity").Value.ShouldBe(Severity.Error);

        var maxBytes = policy.RuleValue<long>(descriptor.Id, "settings.maxBytes");
        maxBytes.Value.ShouldBe(52428800L);
        maxBytes.History.ShouldHaveSingleItem();
        maxBytes.History[0].Value.ShouldBe(5242880L);
    }
}
