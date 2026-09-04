namespace Preflight.Core.Tests.Policy;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core.Policy;

/// <summary>
/// Fixes the seven validation problems policy validation lists, and that
/// validation accumulates every error found across every document instead of
/// stopping at the first one.
/// </summary>
/// <remarks>
/// <see cref="PolicyValidator.ValidateAll"/> never throws; it returns the
/// error list, empty when the load is clean. Deciding whether an empty list
/// means "proceed" and a non-empty one means "abort with
/// <see cref="PolicyValidationException"/>" belongs to the caller
/// (<c>EffectivePolicy.Build</c>), not to the validator itself.
/// </remarks>
public sealed class PolicyValidationTests
{
    private static readonly RuleDescriptor LargeFile = Descriptor("core.presubmit.large-file");
    private static readonly RuleDescriptor Toolchain = Descriptor("core.workspace.toolchain");

    /// <summary>
    /// A version range is walked like any other key: known members only, and the
    /// lower bound is not optional.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>requiresPipeline</c> is the first key whose value is an object with a
    /// shape of its own, and it goes through the schema table rather than being
    /// checked by hand beside it — the drift that a second, hand-written
    /// validation produces is what that table exists to prevent.
    /// </para>
    /// <para>
    /// A range open below is the case worth the assertion. It reads as a bound
    /// and is not one: "any version ever published" is indistinguishable from
    /// having written no key at all, and a checkout carrying it would believe it
    /// was protected from a stale package while accepting every one of them.
    /// </para>
    /// </remarks>
    [Fact]
    public void Validate_WithAVersionRangeMissingItsLowerBound_ReturnsErrorNamingTheMember()
    {
        var document = Document("atlas.json", """
            { "schemaVersion": 1, "pipeline": "atlas", "requiresPipeline": { "maximumVersion": "2.0.0" } }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("minimumVersion");
        errors[0].Message.ShouldContain("requiresPipeline");
        errors[0].FilePath.ShouldBe("atlas.json");
    }

    [Fact]
    public void Validate_WithAnUnknownMemberInsideAVersionRange_ReturnsErrorNamingIt()
    {
        var document = Document("atlas.json", """
            {
              "schemaVersion": 1,
              "pipeline": "atlas",
              "requiresPipeline": { "minimumVersion": "1.0.0", "exactVersion": "1.4.0" }
            }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("exactVersion");
    }

    /// <remarks>
    /// The container check, which the carve-out for <c>settings</c> must not
    /// reach. A scalar where the range belongs would otherwise merge over the
    /// subtree and leave every read of it returning its own fallback.
    /// </remarks>
    [Theory]
    [InlineData("5")]
    [InlineData("\"1.4.0\"")]
    [InlineData("[]")]
    public void Validate_WithAVersionRangeThatIsNotAnObject_ReturnsErrorSayingSo(string value)
    {
        var document = Document("atlas.json", $$"""
            { "schemaVersion": 1, "pipeline": "atlas", "requiresPipeline": {{value}} }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("must be an object");
    }

    [Fact]
    public void Validate_WithACompleteVersionRange_ReturnsNothing()
    {
        var document = Document("atlas.json", """
            {
              "schemaVersion": 1,
              "pipeline": "atlas",
              "requiresPipeline": { "minimumVersion": "1.0.0", "maximumVersion": "2.0.0" }
            }
            """);

        PolicyValidator.ValidateAll([document], [LargeFile]).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_WithRuleKeyNotMatchingAnyDescriptor_ReturnsErrorNamingKeyFileAndClosestSuggestion()
    {
        var document = Document("atlas.json", """
            { "schemaVersion": 1, "rules": { "core.presubmit.large-fil": { "enabled": true } } }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile, Toolchain]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("core.presubmit.large-fil");
        errors[0].Message.ShouldContain("core.presubmit.large-file");
        errors[0].FilePath.ShouldBe("atlas.json");
    }

    [Theory]
    [InlineData("blockin", "blocking")]
    [InlineData("sevirity", "severity")]
    [InlineData("timeotSeconds", "timeoutSeconds")]
    public void Validate_WithUnknownKeyInsideARuleObject_ReturnsErrorWithSuggestion(string typo, string suggestion)
    {
        var document = Document("atlas.json", $$"""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "{{typo}}": true } } }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain(typo);
        errors[0].Message.ShouldContain(suggestion);
    }

    [Fact]
    public void Validate_WithUnknownKeyThatHasNoCloseMatch_ReturnsErrorWithoutASuggestion()
    {
        var document = Document("atlas.json", """
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "zzzzzzzzzz": true } } }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("zzzzzzzzzz");
    }

    [Fact]
    public void Validate_SettingsSubtree_NeverProducesUnknownKeyErrors()
    {
        var document = Document("atlas.json", """
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "settings": { "zzzzzzzzzz": true } } } }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("\"critical\"")]
    [InlineData("1")]
    [InlineData("true")]
    public void Validate_WithInvalidSeverityValue_ReturnsErrorWithFileLineAndJsonPath(string invalidLiteral)
    {
        var document = Document("atlas.json", $$"""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "severity": {{invalidLiteral}} } } }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].FilePath.ShouldBe("atlas.json");
        errors[0].Line.ShouldNotBeNull();
        errors[0].JsonPath.ShouldNotBeNull();
        errors[0].JsonPath!.ShouldContain("severity");
    }

    [Theory]
    [InlineData("blocking", "\"yes\"")]
    [InlineData("gating", "1")]
    [InlineData("enabled", "\"nope\"")]
    public void Validate_WithNonBooleanBlockingGatingOrEnabledValue_ReturnsErrorWithFileLineAndJsonPath(string key, string invalidLiteral)
    {
        var document = Document("atlas.json", $$"""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "{{key}}": {{invalidLiteral}} } } }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].JsonPath!.ShouldContain(key);
    }

    [Theory]
    [InlineData("\"30\"")]
    [InlineData("30.5")]
    public void Validate_WithNonIntegerTimeoutSecondsValue_ReturnsErrorWithFileLineAndJsonPath(string invalidLiteral)
    {
        var document = Document("atlas.json", $$"""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "timeoutSeconds": {{invalidLiteral}} } } }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].JsonPath!.ShouldContain("timeoutSeconds");
    }

    [Fact]
    public void Validate_WithMultipleUnrelatedProblemsInOneDocument_ReturnsAllOfThemInASingleException()
    {
        var document = Document("atlas.json", """
            {
              "schemaVersion": 1,
              "rules": {
                "core.presubmit.large-fil": { "enabled": true },
                "core.workspace.toolchain": { "severity": "critical", "blockin": true }
              }
            }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile, Toolchain]);

        errors.Count.ShouldBe(3);
    }

    [Fact]
    public void Validate_WithProblemsSpreadAcrossBaseAndPipelineDocuments_ReturnsBothInOneException()
    {
        var baseDocument = Document("base.json", """
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "severity": "critical" } } }
            """);
        var productionDocument = Document("atlas.json", """
            { "schemaVersion": 1, "rules": { "core.workspace.toolchain": { "blockin": true } } }
            """);

        var errors = PolicyValidator.ValidateAll([baseDocument, productionDocument], [LargeFile, Toolchain]);

        errors.Count.ShouldBe(2);
        errors.ShouldContain(e => e.FilePath == "base.json");
        errors.ShouldContain(e => e.FilePath == "atlas.json");
    }

    [Fact]
    public void Validate_WithMissingSchemaVersion_ReturnsError()
    {
        var document = Document("atlas.json", """{ "production": "atlas" }""");

        var errors = PolicyValidator.ValidateAll([document], []);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("schemaVersion");
    }

    [Fact]
    public void Validate_WithUnknownSchemaVersion_ReturnsErrorAndSkipsContentValidationOfThatDocument()
    {
        var document = Document("future.json", """
            { "schemaVersion": 2, "rules": { "core.presubmit.large-fil": { "severity": "critical" } } }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("schemaVersion");
    }

    [Theory]
    [InlineData("historyMod", "historyMode")]
    [InlineData("cashePath", "cachePath")]
    [InlineData("maxDegreeOfParalelism", "maxDegreeOfParallelism")]
    public void Validate_WithUnknownRootKey_ReturnsErrorWithSuggestion(string typo, string suggestion)
    {
        var document = Document("atlas.json", $$"""
            { "schemaVersion": 1, "{{typo}}": "whatever" }
            """);

        var errors = PolicyValidator.ValidateAll([document], []);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain(typo);
        errors[0].Message.ShouldContain(suggestion);
        errors[0].JsonPath.ShouldBe(typo);
    }

    [Theory]
    [InlineData("\"sometimes\"")]
    [InlineData("\"Shared\"")]
    [InlineData("1")]
    public void Validate_WithInvalidHistoryModeValue_ReturnsErrorWithFileLineAndJsonPath(string invalidLiteral)
    {
        var document = Document("atlas.json", $$"""
            { "schemaVersion": 1, "historyMode": {{invalidLiteral}} }
            """);

        var errors = PolicyValidator.ValidateAll([document], []);

        errors.ShouldHaveSingleItem();
        errors[0].FilePath.ShouldBe("atlas.json");
        errors[0].Line.ShouldNotBeNull();
        errors[0].JsonPath.ShouldBe("historyMode");
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("per-process")]
    public void Validate_WithEitherDocumentedHistoryMode_ReturnsNoErrors(string historyMode)
    {
        var document = Document("atlas.json", $$"""
            { "schemaVersion": 1, "historyMode": "{{historyMode}}" }
            """);

        PolicyValidator.ValidateAll([document], []).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("maxDegreeOfParallelism", "\"8\"")]
    [InlineData("maxDegreeOfParallelism", "8.5")]
    [InlineData("defaultTimeoutSeconds", "\"60\"")]
    public void Validate_WithNonIntegerRootKeyValue_ReturnsErrorWithFileLineAndJsonPath(string key, string invalidLiteral)
    {
        var document = Document("atlas.json", $$"""
            { "schemaVersion": 1, "{{key}}": {{invalidLiteral}} }
            """);

        var errors = PolicyValidator.ValidateAll([document], []);

        errors.ShouldHaveSingleItem();
        errors[0].JsonPath.ShouldBe(key);
    }

    [Theory]
    [InlineData("historyPath", "42")]
    [InlineData("cachePath", "true")]
    [InlineData("production", "1")]
    public void Validate_WithNonStringRootKeyValue_ReturnsErrorWithFileLineAndJsonPath(string key, string invalidLiteral)
    {
        var document = Document("atlas.json", $$"""
            { "schemaVersion": 1, "{{key}}": {{invalidLiteral}} }
            """);

        var errors = PolicyValidator.ValidateAll([document], []);

        errors.ShouldHaveSingleItem();
        errors[0].JsonPath.ShouldBe(key);
    }

    [Fact]
    public void Validate_WithAFullyPopulatedValidDocument_ReturnsNoErrors()
    {
        var document = Document("atlas.json", """
            {
              "schemaVersion": 1,
              "extends": "preflight.base.json",
              "production": "atlas",
              "maxDegreeOfParallelism": 8,
              "defaultTimeoutSeconds": 60,
              "historyPath": ".preflight/history",
              "historyMode": "shared",
              "cachePath": ".preflight/cache",
              "rules": {
                "core.presubmit.large-file": {
                  "enabled": true,
                  "blocking": true,
                  "gating": false,
                  "severity": "error",
                  "timeoutSeconds": 30,
                  "settings": { "maxBytes": 5242880 }
                }
              }
            }
            """);

        PolicyValidator.ValidateAll([document], [LargeFile]).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_WithProblemsInBothTheRootAndARuleScope_ReturnsBothFromTheOneSharedWalk()
    {
        var document = Document("atlas.json", """
            {
              "schemaVersion": 1,
              "historyMode": "sometimes",
              "rules": { "core.presubmit.large-file": { "severity": "critical" } }
            }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.Count.ShouldBe(2);
        errors.ShouldContain(error => error.JsonPath == "historyMode");
        errors.ShouldContain(error => error.JsonPath == "rules.core.presubmit.large-file.severity");
    }

    [Fact]
    public void Validate_WithRulesKeyAsANonObjectValue_ReturnsExactlyOneErrorAndSkipsRuleMapValidation()
    {
        var document = Document("atlas.json", """{ "schemaVersion": 1, "rules": "oops" }""");

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].JsonPath.ShouldBe("rules");
        errors[0].Message.ShouldContain("must be an object");
    }

    /// <remarks>
    /// This used to pass validation in complete silence. The merge then
    /// replaced the rule's whole subtree with the scalar, and the failure
    /// surfaced much later as an exception while reading an effective value —
    /// policy validation's "failing late is embarrassing, failing silently is
    /// worse", both at once.
    /// </remarks>
    [Fact]
    public void Validate_WithARuleEntryThatIsNotAnObject_ReturnsErrorNamingTheRule()
    {
        var document = Document("atlas.json", """
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": 42 } }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].JsonPath.ShouldBe("rules.core.presubmit.large-file");
        errors[0].Message.ShouldContain("core.presubmit.large-file");
        errors[0].Message.ShouldContain("must be an object");
    }

    /// <remarks>
    /// The policy schema's carve-out is about what is <em>inside</em>
    /// <c>settings</c>, not about whether <c>settings</c> is an object at all.
    /// Exempting the container too let this through, after which every
    /// <c>GetValue</c> the rule made quietly returned its own fallback and
    /// nothing said why.
    /// </remarks>
    [Fact]
    public void Validate_WithSettingsAsANonObjectValue_ReturnsMustBeAnObjectError()
    {
        var document = Document("atlas.json", """
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "settings": 42 } } }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].JsonPath.ShouldBe("rules.core.presubmit.large-file.settings");
        errors[0].Message.ShouldContain("must be an object");
    }

    [Fact]
    public void Validate_WithAScalarKeyGivenAnObjectValue_ReturnsMustBeASingleValueError()
    {
        var document = Document("atlas.json", """
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "blocking": { "nested": true } } } }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("must be a single value, not an object");
    }

    [Fact]
    public void ValidateSetOverride_WithAValidRootKey_ReturnsNoErrors()
    {
        var setOverride = new PolicySetOverride { RuleId = null, Path = "maxDegreeOfParallelism", TypedValue = 4L };

        PolicyValidator.ValidateSetOverride(setOverride, [LargeFile]).ShouldBeEmpty();
    }

    [Fact]
    public void ValidateSetOverride_WithAValidRuleScopedKey_ReturnsNoErrors()
    {
        var setOverride = new PolicySetOverride { RuleId = LargeFile.Id, Path = "blocking", TypedValue = false };

        PolicyValidator.ValidateSetOverride(setOverride, [LargeFile]).ShouldBeEmpty();
    }

    /// <remarks>
    /// Policy precedence puts <c>--set</c> at the top of the precedence chain,
    /// so it is the one layer able to override every other — which made leaving
    /// it unvalidated the sharpest edge in the whole model. This override used
    /// to sail past the key table entirely.
    /// </remarks>
    [Fact]
    public void ValidateSetOverride_TurningARootObjectKeyIntoAScalar_ReturnsMustBeAnObjectError()
    {
        var setOverride = new PolicySetOverride { RuleId = null, Path = "rules", TypedValue = "whatever" };

        var errors = PolicyValidator.ValidateSetOverride(setOverride, [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].JsonPath.ShouldBe("rules");
        errors[0].Message.ShouldContain("--set");
    }

    [Fact]
    public void ValidateSetOverride_TurningARootScalarKeyIntoAnObject_ReturnsMustBeASingleValueError()
    {
        var setOverride = new PolicySetOverride { RuleId = null, Path = "defaultTimeoutSeconds.something", TypedValue = 1L };

        var errors = PolicyValidator.ValidateSetOverride(setOverride, [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("must be a single value, not an object");
    }

    [Fact]
    public void ValidateSetOverride_WithAnUnknownRootKey_ReturnsErrorWithSuggestion()
    {
        var setOverride = new PolicySetOverride { RuleId = null, Path = "historyMod", TypedValue = "shared" };

        var errors = PolicyValidator.ValidateSetOverride(setOverride, [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("historyMode");
    }

    [Fact]
    public void ValidateSetOverride_WithAnUnknownRuleId_ReturnsErrorWithSuggestion()
    {
        var setOverride = new PolicySetOverride
        {
            RuleId = new RuleId("core.presubmit.large-fil"),
            Path = "blocking",
            TypedValue = false,
        };

        var errors = PolicyValidator.ValidateSetOverride(setOverride, [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].JsonPath.ShouldBe("rules.core.presubmit.large-fil");
        errors[0].Message.ShouldContain("core.presubmit.large-file");
    }

    [Fact]
    public void ValidateSetOverride_WithAnUnknownRuleScopedKey_ReturnsErrorWithSuggestion()
    {
        var setOverride = new PolicySetOverride { RuleId = LargeFile.Id, Path = "blockin", TypedValue = false };

        var errors = PolicyValidator.ValidateSetOverride(setOverride, [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("blocking");
    }

    [Fact]
    public void ValidateSetOverride_WithAWrongTypedRuleScopedValue_ReturnsErrorNamingTheKey()
    {
        var setOverride = new PolicySetOverride { RuleId = LargeFile.Id, Path = "blocking", TypedValue = "yes" };

        var errors = PolicyValidator.ValidateSetOverride(setOverride, [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].JsonPath.ShouldBe("rules.core.presubmit.large-file.blocking");
        errors[0].Message.ShouldContain("must be a boolean");
    }

    /// <remarks>
    /// Validation happens at load time, never during execution. Without a
    /// range, zero and negative values pass here and surface much later as an
    /// exception from the middle of a run — <c>Parallel.ForEachAsync</c>
    /// rejects a degree of zero, and a timeout of zero errors every rule
    /// instantly. The upper bound is not taste either: above
    /// <see cref="int.MaxValue"/> the value overflows on its way to a worker
    /// count or a <see cref="TimeSpan"/>.
    /// </remarks>
    [Theory]
    [InlineData("timeoutSeconds", "0")]
    [InlineData("timeoutSeconds", "-1")]
    [InlineData("timeoutSeconds", "99999999999")]
    public void Validate_WithARuleTimeoutOutOfRange_ReportsTheKeyAndTheAllowedRange(string key, string literal)
    {
        var document = Document("atlas.json", $$"""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "{{key}}": {{literal}} } } }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].JsonPath.ShouldBe($"rules.core.presubmit.large-file.{key}");
        errors[0].Message.ShouldContain("must be between");
    }

    [Theory]
    [InlineData("maxDegreeOfParallelism", "0")]
    [InlineData("maxDegreeOfParallelism", "-1")]
    [InlineData("defaultTimeoutSeconds", "0")]
    [InlineData("defaultTimeoutSeconds", "-1")]
    public void Validate_WithARootNumericKeyOutOfRange_ReportsTheKeyAndTheAllowedRange(string key, string literal)
    {
        var document = Document("atlas.json", $$"""
            { "schemaVersion": 1, "{{key}}": {{literal}} }
            """);

        var errors = PolicyValidator.ValidateAll([document], []);

        errors.ShouldHaveSingleItem();
        errors[0].JsonPath.ShouldBe(key);
        errors[0].Message.ShouldContain("must be between");
    }

    /// <remarks>
    /// The boundary itself. Without it the comparison could be written as a
    /// strict inequality and reject the lowest legal value with nobody
    /// noticing.
    /// </remarks>
    [Theory]
    [InlineData("maxDegreeOfParallelism")]
    [InlineData("defaultTimeoutSeconds")]
    public void Validate_WithTheLowestAcceptableRootValue_Accepts(string key)
    {
        var document = Document("atlas.json", $$"""{ "schemaVersion": 1, "{{key}}": 1 }""");

        PolicyValidator.ValidateAll([document], []).ShouldBeEmpty();
    }

    /// <summary>
    /// The former spelling of <c>pipeline</c> still loads.
    /// </summary>
    /// <remarks>
    /// The schema refuses every key it does not list, so removing this one
    /// would turn every policy file written before the rename into a load-time
    /// error — a migration wearing a rename's clothes. And the edit distance
    /// from
    /// <c>production</c> to <c>pipeline</c> is 8 against a suggestion threshold
    /// of 5, so the author would not even be told what to write instead.
    /// </remarks>
    [Fact]
    public void Validate_WithTheDeprecatedProductionRootKey_ReturnsNoErrors()
    {
        var document = Document("atlas.json", """
            { "schemaVersion": 1, "production": "atlas" }
            """);

        PolicyValidator.ValidateAll([document], []).ShouldBeEmpty();
    }

    /// <remarks>
    /// Two spellings of one key define no precedence between them, so honouring
    /// either would decide for the author which of two names they meant. Same
    /// refusal, and the same reason, as <c>--pipeline</c> with
    /// <c>--production</c> on the command line.
    /// </remarks>
    [Fact]
    public void Validate_WithBothPipelineAndProductionAtTheRoot_ReturnsOneErrorNamingBoth()
    {
        var document = Document("atlas.json", """
            {
              "schemaVersion": 1,
              "pipeline": "atlas",
              "production": "atlas"
            }
            """);

        var errors = PolicyValidator.ValidateAll([document], []);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("pipeline");
        errors[0].Message.ShouldContain("production");
        errors[0].FilePath.ShouldBe("atlas.json");
        errors[0].Line.ShouldBe(4);
    }

    /// <remarks>
    /// A key nobody can parse is a block that silently never applies: somebody
    /// wrote a rule for a platform, the run reports success, and the rule was
    /// never in force. <c>any</c> is
    /// in the list because it is the word the CLI uses for "no platform given":
    /// a block keyed on it reads as a wildcard and would mean the literal
    /// string.
    /// </remarks>
    [Theory]
    [InlineData("win64|A|B")]
    [InlineData("|Shipping")]
    [InlineData("any")]
    public void Validate_WithAnUnparseableTargetKey_ReturnsErrorWithFileLineAndTheKey(string key)
    {
        var document = Document("projectc.json", $$"""
            {
              "schemaVersion": 1,
              "targets": { "{{key}}": { "rules": {} } }
            }
            """);

        var errors = PolicyValidator.ValidateAll([document], []);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain(key);
        errors[0].FilePath.ShouldBe("projectc.json");
        errors[0].JsonPath.ShouldBe($"targets.{key}");

        // No line. Line numbers live on leaves, and a target block is an
        // object — the same reason an unknown rule id reports none. The key is
        // in the message and in the JSON path, which is what makes the error
        // actionable; teaching the parser to carry origins on objects is a
        // change to every node in it, for a number two other fields already
        // locate.
        errors[0].Line.ShouldBeNull();
    }

    /// <summary>
    /// Two keys that differ only in case are one key with two spellings.
    /// </summary>
    /// <remarks>
    /// Matching is ordinal and case-insensitive, so both apply at the same
    /// specificity and the winner would come from dictionary order — a value
    /// decided by something nobody wrote down, and different on the next run.
    /// </remarks>
    [Fact]
    public void Validate_WithTwoTargetKeysDifferingOnlyInCase_ReturnsErrorNamingBoth()
    {
        var document = Document("projectc.json", """
            {
              "schemaVersion": 1,
              "targets": {
                "ps5": { "rules": {} },
                "PS5": { "rules": {} }
              }
            }
            """);

        var errors = PolicyValidator.ValidateAll([document], []);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("ps5");
        errors[0].Message.ShouldContain("PS5");
    }

    /// <remarks>
    /// The inside of a target block is a root scope, so an unknown key there is
    /// caught by the same walk that catches one at the root — and a rule id
    /// that does not exist gets the same edit-distance suggestion.
    /// </remarks>
    [Fact]
    public void Validate_WithAnUnknownRuleInsideATargetBlock_ReturnsErrorNamingIt()
    {
        var document = Document("projectc.json", """
            {
              "schemaVersion": 1,
              "targets": {
                "ps5": { "rules": { "core.presubmit.large-fil": { "enabled": true } } }
              }
            }
            """);

        var errors = PolicyValidator.ValidateAll([document], [LargeFile]);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("core.presubmit.large-file");
    }

    [Fact]
    public void Validate_WithTargetsAsANonObjectValue_ReturnsExactlyOneError()
    {
        var document = Document("projectc.json", """{ "schemaVersion": 1, "targets": 42 }""");

        PolicyValidator.ValidateAll([document], []).ShouldHaveSingleItem();
    }

    [Fact]
    public void Validate_WithATargetBlockThatIsNotAnObject_ReturnsErrorNamingTheKey()
    {
        var document = Document("projectc.json", """
            { "schemaVersion": 1, "targets": { "ps5": 42 } }
            """);

        var errors = PolicyValidator.ValidateAll([document], []);

        errors.ShouldHaveSingleItem();
        errors[0].Message.ShouldContain("ps5");
    }

    private static RuleDescriptor Descriptor(string id) => new()
    {
        Id = new RuleId(id),
        DisplayName = id,
        Stage = ValidationStage.PreSubmit,
    };

    private static PolicyDocument Document(string filePath, string json) => PolicyDocument.Parse(json, filePath);
}
