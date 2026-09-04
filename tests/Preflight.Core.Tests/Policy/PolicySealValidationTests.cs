namespace Preflight.Core.Tests.Policy;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core.Policy;

/// <summary>
/// Fixes who a seal refuses, and — just as important — who it does not.
/// </summary>
/// <remarks>
/// A seal that criminalised ordinary policy would take back something the tool
/// already promises: disabling a rule is intended use, not an evasion. Half the
/// entries here exist to prove the seal did not grow teeth it was never given —
/// it forbids changing a value a baseline fixed, and nothing else.
/// </remarks>
public sealed class PolicySealValidationTests
{
    private static readonly RuleDescriptor LargeFile = Descriptor("core.presubmit.large-file");
    private static readonly RuleDescriptor Toolchain = Descriptor("core.workspace.toolchain");

    private static RuleDescriptor Descriptor(string id) => new()
    {
        Id = new RuleId(id),
        DisplayName = id,
        Stage = ValidationStage.PreSubmit,
    };

    private static PolicyDocument Document(string filePath, string json) => PolicyDocument.Parse(json, filePath);

    private static IReadOnlyList<PolicyValidationError> Validate(
        IReadOnlyList<PolicyDocument> chain,
        PolicyDocument? local = null,
        IReadOnlyList<PolicySetOverride>? overrides = null) =>
        PolicyValidator.ValidateSeals(
            PolicySeal.Parse(chain), chain, local, overrides ?? [], [LargeFile, Toolchain]);

    private static readonly PolicyDocument StudioBaseline = Document("studio.json", """
        {
          "schemaVersion": 1,
          "sealed": ["core.workspace.toolchain:blocking"],
          "rules": { "core.workspace.toolchain": { "blocking": true } }
        }
        """);

    [Fact]
    public void ValidateSeals_WithNothingSealed_ReturnsNoErrors()
    {
        var chain = new[]
        {
            Document("studio.json", """{ "schemaVersion": 1 }"""),
            Document("projectc.json", """
                { "schemaVersion": 1, "rules": { "core.workspace.toolchain": { "blocking": false } } }
                """),
        };

        Validate(chain).ShouldBeEmpty();
    }

    [Fact]
    public void ValidateSeals_WithThePipelineOverridingASealSetByItsAncestor_ReturnsErrorNamingBoth()
    {
        var chain = new[]
        {
            StudioBaseline,
            Document("projectc.json", """
                { "schemaVersion": 1, "rules": { "core.workspace.toolchain": { "blocking": false } } }
                """),
        };

        var errors = Validate(chain);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("studio.json");
        errors[0].Message.ShouldContain("core.workspace.toolchain:blocking");
        errors[0].FilePath.ShouldBe("projectc.json");
    }

    /// <remarks>
    /// The baseline states the value it protects, in the same file that
    /// protects it. Refusing that would make a seal impossible to declare
    /// alongside what it seals.
    /// </remarks>
    [Fact]
    public void ValidateSeals_WithTheSealingFileStatingTheValueItself_ReturnsNoErrors() =>
        Validate([StudioBaseline]).ShouldBeEmpty();

    [Fact]
    public void ValidateSeals_WithTheLocalOverlayOverridingASealedKey_ReturnsErrorNamingTheSealingFile()
    {
        var local = Document("preflight.local.json", """
            { "schemaVersion": 1, "rules": { "core.workspace.toolchain": { "blocking": false } } }
            """);

        var errors = Validate([StudioBaseline], local);

        errors.ShouldHaveSingleItem();
        errors[0].FilePath.ShouldBe("preflight.local.json");
        errors[0].Message.ShouldContain("studio.json");
    }

    /// <remarks>
    /// A trap that a line of yaml can spring is not a trap. If <c>--set</c>
    /// were exempt, the seal would protect the files and leave the command line
    /// open, which is the surface a CI script actually edits.
    /// </remarks>
    [Fact]
    public void ValidateSeals_WithASetOverrideOnASealedKey_ReturnsError()
    {
        var overrides = new[]
        {
            new PolicySetOverride
            {
                RuleId = Toolchain.Id,
                Path = "blocking",
                TypedValue = false,
            },
        };

        var errors = Validate([StudioBaseline], local: null, overrides);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("--set");
    }

    [Fact]
    public void ValidateSeals_WithATargetsBlockOverridingASealedKey_ReturnsError()
    {
        var chain = new[]
        {
            StudioBaseline,
            Document("projectc.json", """
                {
                  "schemaVersion": 1,
                  "targets": {
                    "switch2": { "rules": { "core.workspace.toolchain": { "blocking": false } } }
                  }
                }
                """),
        };

        var errors = Validate(chain);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("switch2");
    }

    /// <summary>
    /// Writing the value the seal already fixed is not a violation.
    /// </summary>
    /// <remarks>
    /// Refusing it would turn a file that <em>agrees</em> with the policy into
    /// an error, and the author would be told that agreeing was forbidden. The
    /// comparison is safe here because the schema types every value the parser
    /// produces — a boolean is a boolean and an integer is a long, so equality
    /// means what it looks like.
    /// </remarks>
    [Fact]
    public void ValidateSeals_WritingTheSameValueTheSealFixed_ReturnsNoErrors()
    {
        var chain = new[]
        {
            StudioBaseline,
            Document("projectc.json", """
                { "schemaVersion": 1, "rules": { "core.workspace.toolchain": { "blocking": true } } }
                """),
        };

        Validate(chain).ShouldBeEmpty();
    }

    /// <summary>
    /// A seal on one key leaves every other key of that rule free.
    /// </summary>
    /// <remarks>
    /// Disabling a rule is intended use. A seal implementation that treated any
    /// downstream change to a rule as a violation would quietly withdraw that,
    /// in code, without anybody deciding to.
    /// </remarks>
    [Fact]
    public void ValidateSeals_WithAnUnsealedEnabledSetToFalse_ReturnsNoErrors()
    {
        var chain = new[]
        {
            StudioBaseline,
            Document("projectc.json", """
                {
                  "schemaVersion": 1,
                  "rules": {
                    "core.workspace.toolchain": { "enabled": false, "gating": false, "severity": "warning" }
                  }
                }
                """),
        };

        Validate(chain).ShouldBeEmpty();
    }

    /// <remarks>
    /// The other half of the pair above: the seal does bite when the path was
    /// named. Without both, either result would look like the feature working.
    /// </remarks>
    [Fact]
    public void ValidateSeals_WithASealedEnabledSetToFalse_ReturnsError()
    {
        var chain = new[]
        {
            Document("studio.json", """
                { "schemaVersion": 1, "sealed": ["core.workspace.toolchain:enabled"] }
                """),
            Document("projectc.json", """
                { "schemaVersion": 1, "rules": { "core.workspace.toolchain": { "enabled": false } } }
                """),
        };

        Validate(chain).ShouldHaveSingleItem();
    }

    [Fact]
    public void ValidateSeals_WithAWildcardSeal_CoversEveryRuleWhoseIdMatches()
    {
        var chain = new[]
        {
            Document("studio.json", """{ "schemaVersion": 1, "sealed": ["core.*:blocking"] }"""),
            Document("projectc.json", """
                {
                  "schemaVersion": 1,
                  "rules": {
                    "core.presubmit.large-file": { "blocking": false },
                    "core.workspace.toolchain": { "blocking": false }
                  }
                }
                """),
        };

        Validate(chain).Count.ShouldBe(2);
    }

    [Fact]
    public void ValidateSeals_WithASealedRootKey_RefusesADownstreamOverride()
    {
        var chain = new[]
        {
            Document("studio.json", """{ "schemaVersion": 1, "sealed": [":cachePath"] }"""),
            Document("projectc.json", """{ "schemaVersion": 1, "cachePath": "somewhere-else" }"""),
        };

        Validate(chain).ShouldHaveSingleItem();
    }

    [Fact]
    public void ValidateSeals_WithASealedSettingsPath_RefusesADownstreamOverride()
    {
        var chain = new[]
        {
            Document("studio.json", """
                { "schemaVersion": 1, "sealed": ["core.presubmit.large-file:settings.maxBytes"] }
                """),
            Document("projectc.json", """
                {
                  "schemaVersion": 1,
                  "rules": {
                    "core.presubmit.large-file": { "settings": { "maxBytes": 99999999, "patterns": ["*.uasset"] } }
                  }
                }
                """),
        };

        var errors = Validate(chain);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("settings.maxBytes");
    }

    /// <remarks>
    /// The command line gets the same exemption a file does: repeating the
    /// sealed value is agreement, not an override.
    /// </remarks>
    [Fact]
    public void ValidateSeals_WithASetOverrideWritingTheSealedValue_ReturnsNoErrors()
    {
        var overrides = new[]
        {
            new PolicySetOverride { RuleId = Toolchain.Id, Path = "blocking", TypedValue = true },
        };

        Validate([StudioBaseline], local: null, overrides).ShouldBeEmpty();
    }

    /// <remarks>
    /// A root-key override carries no rule id — <c>--set :cachePath=...</c>,
    /// the same shape the flag already accepts. <c>cachePath</c> is the key
    /// that can be pointed back into the workspace, so a studio baseline that
    /// cannot hold it where it put it has sealed nothing worth sealing.
    /// </remarks>
    [Fact]
    public void ValidateSeals_WithASetOverrideOnASealedRootKey_ReturnsError()
    {
        var chain = new[]
        {
            Document("studio.json", """
                { "schemaVersion": 1, "sealed": [":cachePath"], "cachePath": ".preflight/cache" }
                """),
        };

        var overrides = new[]
        {
            new PolicySetOverride { RuleId = null, Path = "cachePath", TypedValue = "somewhere-else" },
        };

        var errors = PolicyValidator.ValidateSeals(
            PolicySeal.Parse(chain), chain, local: null, overrides, [LargeFile, Toolchain]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain(":cachePath");
    }

    [Fact]
    public void ValidateSeals_WithADocumentWhoseRootIsNotAnObject_ReturnsNoErrors()
    {
        var chain = new[] { StudioBaseline, Document("projectc.json", "42") };

        Validate(chain).ShouldBeEmpty();
    }

    [Fact]
    public void ValidateSeals_WithATargetsKeyThatIsNotAnObject_ReturnsNoErrors()
    {
        var chain = new[]
        {
            StudioBaseline,
            Document("projectc.json", """{ "schemaVersion": 1, "targets": 42 }"""),
        };

        Validate(chain).ShouldBeEmpty();
    }

    [Fact]
    public void ValidateSeals_WithATargetBlockThatIsNotAnObject_ReturnsNoErrors()
    {
        var chain = new[]
        {
            StudioBaseline,
            Document("projectc.json", """{ "schemaVersion": 1, "targets": { "ps5": 42 } }"""),
        };

        Validate(chain).ShouldBeEmpty();
    }

    [Fact]
    public void ValidateSeals_WithARuleEntryThatIsNotAnObject_ReturnsNoErrors()
    {
        var chain = new[]
        {
            StudioBaseline,
            Document("projectc.json", """
                { "schemaVersion": 1, "rules": { "core.workspace.toolchain": 42 } }
                """),
        };

        Validate(chain).ShouldBeEmpty();
    }

    /// <remarks>
    /// Every problem in a load comes back together: a policy with four faults
    /// should cost one edit, not four runs.
    /// </remarks>
    [Fact]
    public void ValidateSeals_WithSeveralViolations_ReturnsAllOfThem()
    {
        var chain = new[]
        {
            Document("studio.json", """
                {
                  "schemaVersion": 1,
                  "sealed": ["core.workspace.toolchain:blocking", "core.presubmit.large-file:blocking"]
                }
                """),
            Document("projectc.json", """
                {
                  "schemaVersion": 1,
                  "rules": {
                    "core.workspace.toolchain": { "blocking": false },
                    "core.presubmit.large-file": { "blocking": false }
                  }
                }
                """),
        };

        Validate(chain).Count.ShouldBe(2);
    }
}
