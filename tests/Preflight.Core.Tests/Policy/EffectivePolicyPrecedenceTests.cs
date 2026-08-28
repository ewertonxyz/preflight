namespace Preflight.Core.Tests.Policy;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core.Policy;

/// <summary>
/// Merge tests covering the whole precedence table.
/// </summary>
/// <remarks>
/// Precedence order (policy precedence): <c>RuleDescriptor</c> defaults →
/// <c>production</c> document → <c>local</c> document → <c>--set</c>. Each
/// layer wins only for the keys it actually touches; untouched keys keep
/// falling through to whichever earlier layer last touched them.
/// </remarks>
public sealed class EffectivePolicyPrecedenceTests
{
    private static readonly RuleDescriptor LargeFile = new()
    {
        Id = new RuleId("core.presubmit.large-file"),
        DisplayName = "Large file",
        Stage = ValidationStage.PreSubmit,
        DefaultSeverity = Severity.Error,
        DefaultBlocking = true,
        DefaultGating = true,
        DefaultTimeoutSeconds = 60,
    };

    [Fact]
    public void Build_WithNoLayersAtAll_UsesOnlyRuleDescriptorDefaults()
    {
        var policy = EffectivePolicy.Build([LargeFile], pipeline: null, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        policy.RuleValue<bool>(LargeFile.Id, "blocking").Value.ShouldBeTrue();
        policy.RuleValue<bool>(LargeFile.Id, "gating").Value.ShouldBeTrue();
        policy.RuleValue<Severity>(LargeFile.Id, "severity").Value.ShouldBe(Severity.Error);
        policy.RuleValue<bool>(LargeFile.Id, "enabled").Value.ShouldBeTrue();
    }

    [Fact]
    public void Build_WithOnlyAPipelineDocument_PipelineValuesOverrideDescriptorDefaults()
    {
        var production = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "blocking": false } } }
            """, "atlas.json");

        var policy = EffectivePolicy.Build([LargeFile], production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        policy.RuleValue<bool>(LargeFile.Id, "blocking").Value.ShouldBeFalse();
        policy.RuleValue<bool>(LargeFile.Id, "gating").Value.ShouldBeTrue();
    }

    [Fact]
    public void Build_WithPipelineAndLocal_LocalOverridesPipelineForTheKeysItTouches_PipelineSurvivesForTheRest()
    {
        var production = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "blocking": false, "severity": "warning" } } }
            """, "atlas.json");
        var local = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "severity": "information" } } }
            """, "local.json");

        var policy = EffectivePolicy.Build([LargeFile], production, local, setOverrides: [], target: StatedBuildTarget.Unstated);

        policy.RuleValue<bool>(LargeFile.Id, "blocking").Value.ShouldBeFalse();
        policy.RuleValue<Severity>(LargeFile.Id, "severity").Value.ShouldBe(Severity.Information);
    }

    [Fact]
    public void Build_WithPipelineLocalAndSet_SetOverridesEverythingBeneathIt()
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

        policy.RuleValue<bool>(LargeFile.Id, "blocking").Value.ShouldBeFalse();
    }

    [Fact]
    public void Build_TheFullPrecedenceTable_EachLayerWinsOnlyForTheKeysItTouches()
    {
        var production = PolicyDocument.Parse("""
            {
              "schemaVersion": 1,
              "rules": {
                "core.presubmit.large-file": {
                  "blocking": false,
                  "severity": "warning",
                  "settings": { "maxBytes": 5242880 }
                }
              }
            }
            """, "atlas.json");
        var local = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "severity": "information", "settings": { "maxBytes": 10485760 } } } }
            """, "local.json");
        var setOverrides = new[]
        {
            new PolicySetOverride { RuleId = LargeFile.Id, Path = "settings.maxBytes", TypedValue = 52428800L },
        };

        var policy = EffectivePolicy.Build([LargeFile], production, local, setOverrides, StatedBuildTarget.Unstated);

        // gating: untouched by every layer -> descriptor default
        policy.RuleValue<bool>(LargeFile.Id, "gating").Value.ShouldBeTrue();
        // blocking: touched only by production -> production
        policy.RuleValue<bool>(LargeFile.Id, "blocking").Value.ShouldBeFalse();
        // severity: touched by production and local -> local
        policy.RuleValue<Severity>(LargeFile.Id, "severity").Value.ShouldBe(Severity.Information);
        // settings.maxBytes: touched by all three -> --set
        policy.RuleValue<long>(LargeFile.Id, "settings.maxBytes").Value.ShouldBe(52428800L);
    }

    [Fact]
    public void Build_WithLocalAbsent_SkipsThatLayerWithoutError()
    {
        var production = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "blocking": false } } }
            """, "atlas.json");

        var policy = EffectivePolicy.Build([LargeFile], production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        policy.RuleValue<bool>(LargeFile.Id, "blocking").Value.ShouldBeFalse();
    }

    [Fact]
    public void RootValue_WithAKeyNoLayerEverSet_ThrowsNamingThePath()
    {
        var policy = EffectivePolicy.Build([LargeFile], pipeline: null, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        Should.Throw<InvalidOperationException>(() => policy.RootValue<long>("thisKeyDoesNotExist"))
            .Message.ShouldContain("thisKeyDoesNotExist");
    }

    [Fact]
    public void RuleValue_RequestingAKeyThatResolvesToAnObjectNotALeaf_ThrowsNamingThePath()
    {
        var policy = EffectivePolicy.Build([LargeFile], pipeline: null, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        Should.Throw<InvalidOperationException>(() => policy.RuleValue<long>(LargeFile.Id, "settings"))
            .Message.ShouldContain("settings");
    }

    [Theory]
    [InlineData(Severity.Information)]
    [InlineData(Severity.Warning)]
    [InlineData(Severity.Error)]
    public void Build_WithVariousDescriptorDefaultSeverities_RoundTripsToTheSameEnumValue(Severity severity)
    {
        var descriptor = LargeFile with { DefaultSeverity = severity };

        var policy = EffectivePolicy.Build([descriptor], pipeline: null, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        policy.RuleValue<Severity>(descriptor.Id, "severity").Value.ShouldBe(severity);
    }

    /// <remarks>
    /// Not a decorative test for an unreachable default arm:
    /// <c>RuleDescriptor.DefaultSeverity</c> is a publicly settable property on
    /// an Abstractions record, and C# does not range-check an enum cast. A
    /// careless descriptor factory in an external plugin can produce this, so
    /// failing loudly at build time is the correct behaviour to pin.
    /// </remarks>
    [Fact]
    public void Build_WithADescriptorDefaultSeverityOutsideTheDefinedEnum_ThrowsArgumentOutOfRangeException()
    {
        var descriptor = LargeFile with { DefaultSeverity = (Severity)99 };

        Should.Throw<ArgumentOutOfRangeException>(
            () => EffectivePolicy.Build([descriptor], pipeline: null, local: null, setOverrides: [], target: StatedBuildTarget.Unstated));
    }

    /// <remarks>
    /// <c>EffectivePolicy.Build</c> deliberately does not re-run
    /// <c>PolicyValidator</c> — the two are separate steps, and every test in
    /// this file reaches Build directly. That makes an unvalidated severity
    /// string reachable here, and it must fail loudly rather than quietly
    /// resolving to <c>Error</c>: a policy that silently ran at a severity
    /// nobody wrote is the false green principle 7 forbids.
    /// </remarks>
    [Fact]
    public void RuleValue_WithASeverityStringOutsideTheDocumentedSet_ThrowsNamingTheValue()
    {
        var production = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "severity": "critical" } } }
            """, "atlas.json");

        var policy = EffectivePolicy.Build([LargeFile], production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        Should.Throw<InvalidOperationException>(() => policy.RuleValue<Severity>(LargeFile.Id, "severity"))
            .Message.ShouldContain("critical");
    }

    [Fact]
    public void RuleValue_WithASeverityStoredAsANonStringRawValue_ThrowsNamingTheActualType()
    {
        var production = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "severity": 1 } } }
            """, "atlas.json");

        var policy = EffectivePolicy.Build([LargeFile], production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        Should.Throw<InvalidOperationException>(() => policy.RuleValue<Severity>(LargeFile.Id, "severity"))
            .Message.ShouldContain("Int64");
    }
}
