namespace Preflight.Core.Tests.Policy;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Core.Policy;

/// <summary>
/// Fixes the observable boundary the policy reader describes: the
/// <see cref="IPolicyReader"/> handed to a rule sees only that rule's own
/// <c>settings</c> object, nothing else.
/// </summary>
/// <remarks>
/// This does not re-test the merge itself (see
/// <c>EffectivePolicyPrecedenceTests</c>) — only that the reader obtained from
/// <c>EffectivePolicy.ReaderFor</c> actually enforces the scoping, including
/// against a rule trying to read its own engine fields or another rule's data.
/// </remarks>
public sealed class ScopedPolicyReaderTests
{
    private static readonly RuleDescriptor RuleA = new()
    {
        Id = new RuleId("core.presubmit.large-file"),
        DisplayName = "Large file",
        Stage = ValidationStage.PreSubmit,
        DefaultBlocking = true,
    };

    private static readonly RuleDescriptor RuleB = new()
    {
        Id = new RuleId("core.workspace.toolchain"),
        DisplayName = "Toolchain",
        Stage = ValidationStage.Workspace,
    };

    private static IPolicyReader ReaderForRuleAWith(string productionJson)
    {
        var production = PolicyDocument.Parse(productionJson, "atlas.json");
        var policy = EffectivePolicy.Build([RuleA, RuleB], production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        return policy.ReaderFor(RuleA.Id);
    }

    [Fact]
    public void GetValue_WithATopLevelSettingsKey_ReturnsTheEffectiveValue()
    {
        var reader = ReaderForRuleAWith("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 1024 } } } }
            """);

        reader.GetValue("maxBytes", 0L).ShouldBe(1024L);
    }

    [Fact]
    public void GetValue_WithADottedNestedPath_ReachesADeepSettingsValue()
    {
        var reader = ReaderForRuleAWith("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "settings": { "limits": { "maxBytes": 2048 } } } } }
            """);

        reader.GetValue("limits.maxBytes", 0L).ShouldBe(2048L);
    }

    [Fact]
    public void GetValue_WithAMissingKey_ReturnsTheFallback()
    {
        var reader = ReaderForRuleAWith("""{ "schemaVersion": 1 }""");

        reader.GetValue("doesNotExist", 42L).ShouldBe(42L);
    }

    [Fact]
    public void TryGetValue_WithAPresentKeyOfTheRequestedType_ReturnsTrueAndTheValue()
    {
        var reader = ReaderForRuleAWith("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 1024 } } } }
            """);

        reader.TryGetValue<long>("maxBytes", out var value).ShouldBeTrue();
        value.ShouldBe(1024L);
    }

    [Fact]
    public void TryGetValue_WithAMissingKey_ReturnsFalse()
    {
        var reader = ReaderForRuleAWith("""{ "schemaVersion": 1 }""");

        reader.TryGetValue<long>("doesNotExist", out _).ShouldBeFalse();
    }

    /// <remarks>
    /// A rule whose <c>settings</c> was overridden to a scalar has, in effect,
    /// no settings at all. The reader treats it as empty rather than throwing —
    /// the loud failure for this belongs to <c>PolicyValidator</c>, at load
    /// time, which is where it now happens.
    /// </remarks>
    [Fact]
    public void GetValue_ForARuleWhoseSettingsWasOverriddenToAScalar_TreatsItAsEmpty()
    {
        var reader = ReaderForRuleAWith("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "settings": 42 } } }
            """);

        reader.GetValue("anything", 7L).ShouldBe(7L);
    }

    [Fact]
    public void TryGetValue_WithAnExplicitNullSetting_RequestedAsANullableReferenceType_ReturnsTrueAndNull()
    {
        var reader = ReaderForRuleAWith("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "settings": { "foo": null } } } }
            """);

        reader.TryGetValue<string>("foo", out var value).ShouldBeTrue();
        value.ShouldBeNull();
    }

    /// <remarks>
    /// Throwing rather than quietly handing back the fallback is deliberate
    /// (see <c>PolicyValueConversion</c>): a fallback returned for a type
    /// mismatch is indistinguishable from one returned for a missing key, and
    /// a rule has no log channel to report the difference. The exception makes
    /// the rule <c>Errored</c>, which is the right status for a defect in the
    /// rule rather than in the workspace.
    /// </remarks>
    [Fact]
    public void TryGetValue_WithAnExplicitNullSetting_RequestedAsANonNullableValueType_Throws()
    {
        var reader = ReaderForRuleAWith("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "settings": { "foo": null } } } }
            """);

        Should.Throw<InvalidOperationException>(() => reader.TryGetValue<long>("foo", out _));
    }

    [Fact]
    public void TryGetValue_WithASettingValueThatFitsInInt_NarrowsFromTheStoredLong()
    {
        var reader = ReaderForRuleAWith("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "settings": { "count": 5 } } } }
            """);

        reader.TryGetValue<int>("count", out var value).ShouldBeTrue();
        value.ShouldBe(5);
    }

    /// <remarks>
    /// <c>settings</c> is opaque by contract (the policy schema), so the key schema
    /// structurally cannot bound what goes in it — a human can legitimately
    /// write a number that fits a <c>long</c> and not an <c>int</c>. The
    /// <c>checked</c> cast turning that into a loud failure is the design, not
    /// a gap in validation.
    /// </remarks>
    [Fact]
    public void TryGetValue_WithASettingValueThatOverflowsInt_ThrowsOverflowException()
    {
        var reader = ReaderForRuleAWith("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "settings": { "count": 9999999999 } } } }
            """);

        Should.Throw<OverflowException>(() => reader.TryGetValue<int>("count", out _));
    }

    [Fact]
    public void GetValue_ForARootPolicyKey_ReturnsTheFallback_RootKeysAreNotReachable()
    {
        var reader = ReaderForRuleAWith("""{ "schemaVersion": 1, "maxDegreeOfParallelism": 8 }""");

        reader.GetValue("maxDegreeOfParallelism", -1L).ShouldBe(-1L);
    }

    [Fact]
    public void GetValue_ForAnotherRulesSettingsKey_ReturnsTheFallback_CrossRuleIsolation()
    {
        var production = PolicyDocument.Parse("""
            {
              "schemaVersion": 1,
              "rules": {
                "core.presubmit.large-file": { "settings": { "maxBytes": 1024 } },
                "core.workspace.toolchain": { "settings": { "requiredVersion": "1.2.3" } }
              }
            }
            """, "atlas.json");
        var policy = EffectivePolicy.Build([RuleA, RuleB], production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        var readerForA = policy.ReaderFor(RuleA.Id);

        readerForA.TryGetValue<string>("requiredVersion", out _).ShouldBeFalse();
    }

    [Fact]
    public void GetValue_ForAnEngineFieldOfItsOwnRule_ReturnsTheFallback_EngineFieldsAreNotReachableViaThisInterface()
    {
        var reader = ReaderForRuleAWith("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "blocking": false } } }
            """);

        // The effective value of "blocking" is false. A fallback of true that
        // still comes back proves the reader never reached the real field.
        reader.GetValue("blocking", true).ShouldBeTrue();
        reader.TryGetValue<bool>("blocking", out _).ShouldBeFalse();
    }
}
