namespace Preflight.Core.Tests.Policy;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core.Policy;

/// <summary>
/// Fixes the second axis of precedence: a rule's <c>timeoutSeconds</c> falls
/// back to the root <c>defaultTimeoutSeconds</c>, and that fallback resolves
/// by specificity rather than by layer order.
/// </summary>
/// <remarks>
/// the policy schema types the rule key as
/// "default = <c>raiz.defaultTimeoutSeconds</c>", which crosses the layer
/// chain of policy precedence instead of sitting inside it. The two axes point
/// different ways, which is exactly why this needs its own tests rather than
/// being folded into <c>EffectivePolicyPrecedenceTests</c>: a rule that names
/// its own budget keeps it even when a <em>later</em> layer sets the root
/// default.
/// </remarks>
public sealed class EffectivePolicyTimeoutCascadeTests
{
    private static readonly RuleDescriptor SlowRule = new()
    {
        Id = new RuleId("core.build.compile-probe"),
        DisplayName = "Compile probe",
        Stage = ValidationStage.BuildReadiness,
        DefaultTimeoutSeconds = 300,
    };

    [Fact]
    public void Build_WhenNoLayerSetsEitherKey_TheRuleKeepsItsDescriptorDefault()
    {
        var policy = EffectivePolicy.Build([SlowRule], pipeline: null, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);
        var timeout = policy.RuleValue<long>(SlowRule.Id, "timeoutSeconds");

        timeout.Value.ShouldBe(300L);
        timeout.Origin.ShouldBeOfType<PolicyOrigin.DescriptorDefault>();
    }

    [Fact]
    public void Build_WhenTheRootDefaultIsSetAndTheRuleIsSilent_TheRuleInheritsTheRootValue()
    {
        var production = PolicyDocument.Parse("""
            { "schemaVersion": 1, "defaultTimeoutSeconds": 45 }
            """, "atlas.json");

        var policy = EffectivePolicy.Build([SlowRule], production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        policy.RuleValue<long>(SlowRule.Id, "timeoutSeconds").Value.ShouldBe(45L);
    }

    [Fact]
    public void Build_WhenTheRuleInheritsTheRootValue_TheOriginNamesTheRootKeyAndItsOwnSource()
    {
        var production = PolicyDocument.Parse("""
            { "schemaVersion": 1, "defaultTimeoutSeconds": 45 }
            """, "atlas.json");

        var policy = EffectivePolicy.Build([SlowRule], production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);
        var origin = policy.RuleValue<long>(SlowRule.Id, "timeoutSeconds").Origin
            .ShouldBeOfType<PolicyOrigin.FromRootKey>();

        origin.RootKey.ShouldBe("defaultTimeoutSeconds");
        origin.Source.ShouldBeOfType<PolicyOrigin.FromFile>().FilePath.ShouldBe("atlas.json");
    }

    [Fact]
    public void Build_WhenTheRuleInheritsTheRootValue_ItsHistoryStillShowsTheDescriptorDefaultItReplaced()
    {
        var production = PolicyDocument.Parse("""
            { "schemaVersion": 1, "defaultTimeoutSeconds": 45 }
            """, "atlas.json");

        var policy = EffectivePolicy.Build([SlowRule], production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);
        var timeout = policy.RuleValue<long>(SlowRule.Id, "timeoutSeconds");

        timeout.History.ShouldHaveSingleItem();
        timeout.History[0].Value.ShouldBe(300L);
        timeout.History[0].Origin.ShouldBeOfType<PolicyOrigin.DescriptorDefault>();
    }

    [Fact]
    public void Build_WhenBothTheRootDefaultAndTheRulesOwnValueAreSet_TheRuleSpecificValueWins()
    {
        var production = PolicyDocument.Parse("""
            {
              "schemaVersion": 1,
              "defaultTimeoutSeconds": 45,
              "rules": { "core.build.compile-probe": { "timeoutSeconds": 120 } }
            }
            """, "atlas.json");

        var policy = EffectivePolicy.Build([SlowRule], production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);
        var timeout = policy.RuleValue<long>(SlowRule.Id, "timeoutSeconds");

        timeout.Value.ShouldBe(120L);
        timeout.Origin.ShouldBeOfType<PolicyOrigin.FromFile>();
    }

    [Fact]
    public void Build_WhenALaterLayerSetsOnlyTheRootDefault_ARuleSpecificValueFromAnEarlierLayerStillWins()
    {
        // Specificity beats layer order. Adding one line to a local overlay
        // must not silently retune a rule that was deliberately given its own
        // budget in the production file.
        var production = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.build.compile-probe": { "timeoutSeconds": 120 } } }
            """, "atlas.json");
        var local = PolicyDocument.Parse("""
            { "schemaVersion": 1, "defaultTimeoutSeconds": 5 }
            """, "local.json");

        var policy = EffectivePolicy.Build([SlowRule], production, local, setOverrides: [], target: StatedBuildTarget.Unstated);

        policy.RuleValue<long>(SlowRule.Id, "timeoutSeconds").Value.ShouldBe(120L);
    }

    [Fact]
    public void Build_WhenTheRootDefaultIsSet_ItOutranksTheDescriptorDefault()
    {
        // The rule descriptor: every Default-prefixed descriptor field is only a
        // default, and policy has the final word — even when policy speaks at
        // the root rather than about this rule.
        var production = PolicyDocument.Parse("""
            { "schemaVersion": 1, "defaultTimeoutSeconds": 10 }
            """, "atlas.json");

        var policy = EffectivePolicy.Build([SlowRule], production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        policy.RuleValue<long>(SlowRule.Id, "timeoutSeconds").Value.ShouldBe(10L);
    }

    [Fact]
    public void Build_CascadesTheRootDefaultToEveryRuleThatIsSilent_NotJustTheFirst()
    {
        var otherRule = new RuleDescriptor
        {
            Id = new RuleId("core.presubmit.large-file"),
            DisplayName = "Large file",
            Stage = ValidationStage.PreSubmit,
            DefaultTimeoutSeconds = 60,
        };

        var production = PolicyDocument.Parse("""
            { "schemaVersion": 1, "defaultTimeoutSeconds": 45 }
            """, "atlas.json");

        var policy = EffectivePolicy.Build([SlowRule, otherRule], production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        policy.RuleValue<long>(SlowRule.Id, "timeoutSeconds").Value.ShouldBe(45L);
        policy.RuleValue<long>(otherRule.Id, "timeoutSeconds").Value.ShouldBe(45L);
    }

    [Fact]
    public void Build_WhenTheRootDefaultComesFromASetOverride_ASilentRuleStillInheritsIt()
    {
        var setOverrides = new[]
        {
            new PolicySetOverride { RuleId = null, Path = "defaultTimeoutSeconds", TypedValue = 7L },
        };

        var policy = EffectivePolicy.Build([SlowRule], pipeline: null, local: null, setOverrides, StatedBuildTarget.Unstated);
        var timeout = policy.RuleValue<long>(SlowRule.Id, "timeoutSeconds");

        timeout.Value.ShouldBe(7L);
        timeout.Origin.ShouldBeOfType<PolicyOrigin.FromRootKey>()
            .Source.ShouldBeOfType<PolicyOrigin.FromCommandLine>();
    }

    /// <remarks>
    /// The three cases below drive the cascade's guard clause through inputs
    /// whose shape disagrees with the base tree. None of them is a policy file
    /// anyone would write on purpose — the point is that the cascade declines
    /// to run rather than throwing on the way past. Note what they also show:
    /// two of them are <c>--set</c> overrides, and until
    /// <c>PolicyValidator.ValidateSetOverride</c> existed, nothing upstream
    /// would have rejected them.
    /// </remarks>
    [Fact]
    public void Build_WhenThePipelineDocumentRootIsNotAnObject_SkipsTheCascadeWithoutThrowing()
    {
        var production = PolicyDocument.Parse("42", "atlas.json");

        Should.NotThrow(() => EffectivePolicy.Build([SlowRule], production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated));
    }

    [Fact]
    public void Build_WhenASetOverrideTurnsTheRootDefaultIntoAnObject_SkipsTheCascadeWithoutThrowing()
    {
        var setOverrides = new[]
        {
            new PolicySetOverride { RuleId = null, Path = "defaultTimeoutSeconds.something", TypedValue = 1L },
        };

        Should.NotThrow(() => EffectivePolicy.Build([SlowRule], pipeline: null, local: null, setOverrides, StatedBuildTarget.Unstated));
    }

    [Fact]
    public void Build_WhenASetOverrideTurnsRulesIntoAScalar_SkipsTheCascadeWithoutThrowing()
    {
        var setOverrides = new[]
        {
            new PolicySetOverride { RuleId = null, Path = "rules", TypedValue = "whatever" },
        };

        Should.NotThrow(() => EffectivePolicy.Build([SlowRule], pipeline: null, local: null, setOverrides, StatedBuildTarget.Unstated));
    }

    [Fact]
    public void Build_WhenOnlyTheRulesOwnValueIsSet_TheRootDefaultStaysAtItsEngineDefault()
    {
        var production = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.build.compile-probe": { "timeoutSeconds": 120 } } }
            """, "atlas.json");

        var policy = EffectivePolicy.Build([SlowRule], production, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        policy.RuleValue<long>(SlowRule.Id, "timeoutSeconds").Value.ShouldBe(120L);
        policy.RootValue<long>("defaultTimeoutSeconds").Value.ShouldBe(60L);
        policy.RootValue<long>("defaultTimeoutSeconds").Origin.ShouldBeOfType<PolicyOrigin.EngineDefault>();
    }
}
