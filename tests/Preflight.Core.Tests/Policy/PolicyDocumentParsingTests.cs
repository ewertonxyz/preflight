namespace Preflight.Core.Tests.Policy;

using System.Text.Json;
using Preflight.Core.Policy;

/// <summary>
/// Fixes the purely syntactic side of <see cref="PolicyDocument.Parse"/>: JSON
/// with comments and trailing commas, and per-leaf file/line provenance
/// captured while reading. Nothing here validates <c>schemaVersion</c> or any
/// rule key — that is <see cref="PolicyValidator"/>'s job (see
/// <c>PolicyValidationTests</c>), kept deliberately separate so a syntactically
/// valid but semantically wrong document can still be parsed and inspected.
/// </summary>
/// <remarks>the policy schema.</remarks>
public sealed class PolicyDocumentParsingTests
{
    [Fact]
    public void Parse_WithLineAndBlockComments_SucceedsAndIgnoresComments()
    {
        const string Json = """
            {
              // a line comment
              "schemaVersion": 1,
              /* a block
                 comment */
              "production": "atlas"
            }
            """;

        var document = PolicyDocument.Parse(Json, "atlas.json");

        document.TryGetRaw("schemaVersion", out var schemaVersion).ShouldBeTrue();
        schemaVersion.ShouldBe(1L);
        document.TryGetRaw("production", out var production).ShouldBeTrue();
        production.ShouldBe("atlas");
    }

    [Fact]
    public void Parse_WithTrailingCommasInObjectsAndArrays_Succeeds()
    {
        const string Json = """
            {
              "schemaVersion": 1,
              "rules": {
                "core.workspace.toolchain": {
                  "enabled": true,
                },
              },
            }
            """;

        var document = PolicyDocument.Parse(Json, "atlas.json");

        document.TryGetRaw(["rules", "core.workspace.toolchain", "enabled"], out var enabled).ShouldBeTrue();
        enabled.ShouldBe(true);
    }

    [Fact]
    public void Parse_CapturesTheOneBasedLineNumberOfEachLeaf()
    {
        const string Json = """
            {
              "schemaVersion": 1,
              "rules": {
                "core.presubmit.large-file": {
                  "settings": {
                    "maxBytes": 52428800
                  }
                }
              }
            }
            """;

        var document = PolicyDocument.Parse(Json, "atlas.json");

        document.Root.TryGetPath(["rules", "core.presubmit.large-file", "settings", "maxBytes"], out var node).ShouldBeTrue();
        var leaf = (PolicyNode.Leaf)node!;
        var origin = (PolicyOrigin.FromFile)leaf.Value.Origin;

        origin.FilePath.ShouldBe("atlas.json");
        origin.Line.ShouldBe(6);
    }

    [Fact]
    public void Parse_WithSchemaVersionOne_ExposesItAsARawValueWithoutValidatingIt()
    {
        var document = PolicyDocument.Parse("""{ "schemaVersion": 2 }""", "future.json");

        document.TryGetRaw("schemaVersion", out var schemaVersion).ShouldBeTrue();
        schemaVersion.ShouldBe(2L);
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("per-process")]
    public void Parse_HistoryModeRawStrings_RoundTripAsPlainStringsWithoutConversion(string rawValue)
    {
        var document = PolicyDocument.Parse($$"""{ "schemaVersion": 1, "historyMode": "{{rawValue}}" }""", "atlas.json");

        document.TryGetRaw("historyMode", out var historyMode).ShouldBeTrue();
        historyMode.ShouldBe(rawValue);
    }

    /// <remarks>
    /// <c>PolicyNodeTests</c> already covers an explicit null at the merge
    /// level, but through hand-built nodes. This is the parsing half of the
    /// same behaviour: a <c>null</c> written in a real policy file has to
    /// survive the reader as a present key holding null, not as an absent key.
    /// </remarks>
    [Fact]
    public void Parse_WithExplicitJsonNull_ReturnsNullFromTryGetRaw()
    {
        var document = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "settings": { "foo": null } } } }
            """, "atlas.json");

        document.TryGetRaw(["rules", "core.presubmit.large-file", "settings", "foo"], out var value).ShouldBeTrue();
        value.ShouldBeNull();
    }

    [Fact]
    public void Parse_ArrayOfAllStrings_ReturnsAStringArray()
    {
        var document = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "settings": { "tags": ["a", "b"] } } } }
            """, "atlas.json");

        document.TryGetRaw(["rules", "core.presubmit.large-file", "settings", "tags"], out var value).ShouldBeTrue();
        value.ShouldBeOfType<string[]>().ShouldBe(["a", "b"]);
    }

    [Fact]
    public void Parse_ArrayWithMixedTypes_ReturnsAnObjectArray()
    {
        var document = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "settings": { "tags": [1, "a"] } } } }
            """, "atlas.json");

        document.TryGetRaw(["rules", "core.presubmit.large-file", "settings", "tags"], out var value).ShouldBeTrue();
        value.ShouldBeOfType<object?[]>().ShouldBe([1L, "a"]);
    }

    [Fact]
    public void Parse_EmptyArray_ReturnsAnEmptyArray()
    {
        var document = PolicyDocument.Parse("""
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "settings": { "tags": [] } } } }
            """, "atlas.json");

        document.TryGetRaw(["rules", "core.presubmit.large-file", "settings", "tags"], out var value).ShouldBeTrue();
        value.ShouldBeOfType<string[]>().ShouldBeEmpty();
    }

    /// <remarks>
    /// A bare scalar at the top level is the one input whose value starts at
    /// byte zero, which is the exact-match side of the line-number lookup.
    /// Parsing it is not itself legal policy — <c>PolicyValidator</c> rejects
    /// it for having no <c>schemaVersion</c> — but <c>Parse</c> is deliberately
    /// syntax-only and must not decide that.
    /// </remarks>
    [Fact]
    public void Parse_WhenTheDocumentRootItselfIsAScalarValue_CapturesLineOneForIt()
    {
        var document = PolicyDocument.Parse("42", "x.json");

        var leaf = document.Root.ShouldBeOfType<PolicyNode.Leaf>();
        var origin = leaf.Value.Origin.ShouldBeOfType<PolicyOrigin.FromFile>();

        origin.Line.ShouldBe(1);
    }

    /// <remarks>
    /// An empty or token-less policy file is an ordinary real-world accident —
    /// a truncated write, a bad merge, a placeholder someone meant to fill in.
    /// The reader rejects all of these from <c>Read</c> itself rather than
    /// yielding a <c>None</c> token, which is why the value switch's default
    /// arm is unreachable and marked as such rather than tested.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("// only a comment")]
    public void Parse_WithNoJsonTokens_ThrowsJsonException(string content)
    {
        Should.Throw<JsonException>(() => PolicyDocument.Parse(content, "x.json"));
    }
}
