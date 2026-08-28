namespace Preflight.Core.Tests.Policy;

using Preflight.Core.Policy;

/// <summary>
/// Fixes the grammar of a seal and what it covers.
/// </summary>
/// <remarks>
/// The separator is <c>:</c> because a rule id contains dots, which is the same
/// ambiguity <c>--set</c> resolved the same way. See ADR-031.
/// </remarks>
public sealed class PolicySealTests
{
    private static PolicyDocument Document(string filePath, string json) => PolicyDocument.Parse(json, filePath);

    private static PolicyDocument Sealing(string filePath, params string[] patterns) =>
        Document(filePath, $$"""
            {
              "schemaVersion": 1,
              "sealed": [{{string.Join(",", patterns.Select(pattern => $"\"{pattern}\""))}}]
            }
            """);

    [Theory]
    [InlineData("core.workspace.toolchain:blocking", "core.workspace.toolchain", "blocking")]
    [InlineData("core.presubmit.large-file:settings.maxBytes", "core.presubmit.large-file", "settings.maxBytes")]
    [InlineData("security.*:enabled", "security.*", "enabled")]
    [InlineData(":cachePath", "", "cachePath")]
    public void TryParse_WithAWellFormedPattern_Accepts(string text, string ruleIdPattern, string keyPath)
    {
        SealPattern.TryParse(text, out var pattern).ShouldBeTrue();

        pattern.RuleIdPattern.ShouldBe(ruleIdPattern);
        pattern.KeyPath.ShouldBe(keyPath);
    }

    /// <remarks>
    /// <c>*</c> only ends a rule id. In the middle it would be a glob, which is
    /// a pattern language to write and test; on the right of the separator it
    /// would seal a rule's whole shape, and <c>blocking</c> and <c>gating</c>
    /// are distinct axes that a single seal must not collapse.
    /// </remarks>
    [Theory]
    [InlineData("core.large-file.blocking")]
    [InlineData("core.*.x:enabled")]
    [InlineData("x:*")]
    [InlineData("")]
    [InlineData("x:")]
    [InlineData(":")]
    [InlineData("a:b:c")]
    public void TryParse_WithAMalformedPattern_IsRefused(string text) =>
        SealPattern.TryParse(text, out _).ShouldBeFalse();

    [Theory]
    [InlineData("security.secrets", "enabled", true)]
    [InlineData("security.secrets", "blocking", false)]
    [InlineData("core.presubmit.large-file", "enabled", false)]
    public void Covers_WithAWildcardId_MatchesByPrefixOnly(string ruleId, string key, bool expected)
    {
        SealPattern.TryParse("security.*:enabled", out var pattern).ShouldBeTrue();

        pattern.Covers(ruleId, key).ShouldBe(expected);
    }

    /// <summary>
    /// Sealing <c>blocking</c> does not seal <c>gating</c>.
    /// </summary>
    /// <remarks>
    /// The live false friend. Section 7.2 makes them separate axes on purpose,
    /// and a seal that covered both would hand out protection nobody asked for
    /// while looking precise — invisible until the quarter it matters.
    /// </remarks>
    [Fact]
    public void IsSealed_ForBlocking_DoesNotSealGating()
    {
        var seal = PolicySeal.Parse([Sealing("studio.json", "core.workspace.toolchain:blocking")]);

        seal.IsSealed("core.workspace.toolchain", "blocking", out _).ShouldBeTrue();
        seal.IsSealed("core.workspace.toolchain", "gating", out _).ShouldBeFalse();
    }

    [Fact]
    public void Parse_WithNoSealedKey_IsEmpty() =>
        PolicySeal.Parse([Document("studio.json", """{ "schemaVersion": 1 }""")]).IsEmpty.ShouldBeTrue();

    [Fact]
    public void Parse_WithAnEmptySealedArray_IsEmpty() =>
        PolicySeal.Parse([Sealing("studio.json")]).IsEmpty.ShouldBeTrue();

    [Fact]
    public void Parse_WithSealsInBothAncestorAndDescendant_UnionsThem()
    {
        var seal = PolicySeal.Parse(
        [
            Sealing("studio.json", "a.b.c:blocking"),
            Sealing("projectc.json", "d.e.f:gating"),
        ]);

        seal.IsSealed("a.b.c", "blocking", out _).ShouldBeTrue();
        seal.IsSealed("d.e.f", "gating", out _).ShouldBeTrue();
    }

    /// <summary>
    /// A descendant cannot drop an ancestor's seal.
    /// </summary>
    /// <remarks>
    /// The gravest entry in this file. <c>PolicyNode.Merge</c> replaces a
    /// stronger leaf whole, so a pipeline declaring its own <c>sealed</c> array
    /// would erase the studio baseline's — and a baseline that silently stops
    /// sealing is a governance false green, which is the exact thing the
    /// feature exists to remove.
    /// </remarks>
    [Fact]
    public void Parse_WithADescendantThatDeclaresItsOwnSeals_KeepsTheAncestorSeals()
    {
        var seal = PolicySeal.Parse(
        [
            Sealing("studio.json", "a.b.c:blocking"),
            Sealing("projectc.json", "d.e.f:gating"),
        ]);

        seal.IsSealed("a.b.c", "blocking", out var source).ShouldBeTrue();
        source.FilePath.ShouldBe("studio.json");
        source.Pattern.ShouldBe("a.b.c:blocking");
    }

    [Fact]
    public void Parse_WithADescendantThatDeclaresAnEmptyArray_KeepsTheAncestorSeals()
    {
        var seal = PolicySeal.Parse([Sealing("studio.json", "a.b.c:blocking"), Sealing("projectc.json")]);

        seal.IsSealed("a.b.c", "blocking", out _).ShouldBeTrue();
    }

    [Fact]
    public void Parse_WithTheSamePatternInTwoFiles_AttributesItToTheEarlierOne()
    {
        var seal = PolicySeal.Parse(
        [
            Sealing("studio.json", "a.b.c:blocking"),
            Sealing("projectc.json", "a.b.c:blocking"),
        ]);

        seal.IsSealed("a.b.c", "blocking", out var source).ShouldBeTrue();
        source.FilePath.ShouldBe("studio.json");
    }

    [Fact]
    public void Parse_WithAMalformedPattern_SkipsItRatherThanThrowing()
    {
        var seal = PolicySeal.Parse([Sealing("studio.json", "nonsense", "a.b.c:blocking")]);

        seal.IsSealed("a.b.c", "blocking", out _).ShouldBeTrue();
    }

    [Fact]
    public void IsSealed_ForARootKey_MatchesTheEmptyIdForm()
    {
        var seal = PolicySeal.Parse([Sealing("studio.json", ":cachePath")]);

        seal.IsSealed(ruleId: null, "cachePath", out _).ShouldBeTrue();
        seal.IsSealed(ruleId: null, "historyPath", out _).ShouldBeFalse();
        seal.IsSealed("a.b.c", "cachePath", out _).ShouldBeFalse();
    }

    [Fact]
    public void IsSealed_ForASettingsPath_ReachesInsideSettings()
    {
        var seal = PolicySeal.Parse([Sealing("studio.json", "core.presubmit.large-file:settings.maxBytes")]);

        seal.IsSealed("core.presubmit.large-file", "settings.maxBytes", out _).ShouldBeTrue();
        seal.IsSealed("core.presubmit.large-file", "settings.patterns", out _).ShouldBeFalse();
        seal.IsSealed("core.presubmit.large-file", "settings", out _).ShouldBeFalse();
    }

    /// <remarks>
    /// A rule-scoped seal never covers a root key of the same name. Both live
    /// in the same document and the walk visits both, so without this the seal
    /// on a rule's <c>cachePath</c> setting would refuse a legitimate root
    /// <c>cachePath</c>.
    /// </remarks>
    [Fact]
    public void IsSealed_ForARuleScopedPattern_DoesNotCoverARootKeyOfTheSameName()
    {
        var seal = PolicySeal.Parse([Sealing("studio.json", "a.b.c:cachePath")]);

        seal.IsSealed("a.b.c", "cachePath", out _).ShouldBeTrue();
        seal.IsSealed(ruleId: null, "cachePath", out _).ShouldBeFalse();
    }

    /// <remarks>
    /// A <c>sealed</c> key that is not an array is a schema error, reported by
    /// the validator. This one only has to not seal anything and not throw:
    /// two errors for one mistake read as two mistakes.
    /// </remarks>
    [Theory]
    [InlineData("""{ "schemaVersion": 1, "sealed": 42 }""")]
    [InlineData("""{ "schemaVersion": 1, "sealed": { "a": 1 } }""")]
    public void Parse_WithASealedKeyThatIsNotAnArray_SealsNothing(string json) =>
        PolicySeal.Parse([Document("studio.json", json)]).IsEmpty.ShouldBeTrue();

    [Fact]
    public void Parse_WithADocumentWhoseRootIsNotAnObject_SealsNothing() =>
        PolicySeal.Parse([Document("studio.json", "42")]).IsEmpty.ShouldBeTrue();

    /// <summary>
    /// A file is not bound by a seal it declared itself.
    /// </summary>
    /// <remarks>
    /// Otherwise a studio baseline could not state the value it is protecting:
    /// it would seal <c>blocking</c> and then be refused for saying
    /// <c>"blocking": true</c> in the same file.
    /// </remarks>
    [Fact]
    public void IsSealed_AfterTheDeclaringFileItself_IsFalse()
    {
        var seal = PolicySeal.Parse([Sealing("studio.json", "a.b.c:blocking")]);

        seal.IsSealed("a.b.c", "blocking", out _, afterFilePath: "studio.json").ShouldBeFalse();
        seal.IsSealed("a.b.c", "blocking", out _, afterFilePath: "projectc.json").ShouldBeTrue();
    }
}
