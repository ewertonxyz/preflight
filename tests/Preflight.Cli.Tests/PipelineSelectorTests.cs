namespace Preflight.Cli.Tests;

using Preflight.Core.Policy;

/// <summary>
/// Fixes which pipeline a run validates against when nobody named one.
/// </summary>
/// <remarks>
/// Against a real temporary directory rather than a substituted file system:
/// what this type does is enumerate a directory and read one file, and a
/// substitute would let a wrong glob pattern pass. The candidate list is the
/// part that decides everything else. See ADR-029.
/// </remarks>
public sealed class PipelineSelectorTests : IDisposable
{
    private readonly DirectoryInfo _workspace = Directory.CreateTempSubdirectory("preflight-select-");
    private readonly Preflight.Core.PhysicalFileSystem _fileSystem = new();

    public void Dispose() => _workspace.Delete(recursive: true);

    private void Write(string name, string content) =>
        File.WriteAllText(Path.Combine(_workspace.FullName, name), content);

    private PipelineSelection Select(string? explicitPipeline = null) =>
        PipelineSelector.Select(
            _workspace, _fileSystem, explicitPipeline, TestContext.Current.CancellationToken);

    [Fact]
    public void Select_WithAnExplicitFlag_PrefersItOverTheCheckoutKey()
    {
        Write("preflight.base.json", """{ "schemaVersion": 1, "pipeline": "atlas" }""");
        Write("preflight.switch2.json", """{ "schemaVersion": 1 }""");

        var selection = Select("switch2");

        selection.Pipeline.ShouldBe("switch2");
        selection.Source.ShouldBe(PipelineSource.CommandLine);
    }

    [Fact]
    public void Select_WithNoFlagAndAPipelineKeyInBase_ReturnsTheKeyAsCheckout()
    {
        Write("preflight.base.json", """{ "schemaVersion": 1, "pipeline": "atlas" }""");

        var selection = Select();

        selection.Pipeline.ShouldBe("atlas");
        selection.Source.ShouldBe(PipelineSource.Checkout);
    }

    /// <remarks>
    /// ADR-027 kept <c>production</c> as an accepted spelling of the root key,
    /// so the checkout key has to read it too — otherwise a policy file written
    /// before that ADR would parse, validate, and select nothing.
    /// </remarks>
    [Fact]
    public void Select_WithTheDeprecatedProductionKeyInBase_ReturnsItAsCheckout()
    {
        Write("preflight.base.json", """{ "schemaVersion": 1, "production": "atlas" }""");

        Select().Pipeline.ShouldBe("atlas");
    }

    /// <summary>
    /// Several pipelines and no choice is a refusal, not a fall back.
    /// </summary>
    /// <remarks>
    /// Falling back to the base would validate against a policy nobody
    /// selected and report success — the false green of principle 7, produced
    /// by a workspace that grew a second pipeline. The candidates are named
    /// because a refusal is worth more when it says what would have worked.
    /// </remarks>
    [Fact]
    public void Select_WithNoFlagNoKeyAndSeveralCandidates_ThrowsListingThemInOrdinalOrder()
    {
        Write("preflight.base.json", """{ "schemaVersion": 1 }""");
        Write("preflight.switch2.json", """{ "schemaVersion": 1 }""");
        Write("preflight.atlas.json", """{ "schemaVersion": 1 }""");

        var exception = Should.Throw<PolicyValidationException>(() => Select());

        var message = exception.Message;

        message.ShouldContain("atlas");
        message.ShouldContain("switch2");
        message.IndexOf("atlas", StringComparison.Ordinal)
            .ShouldBeLessThan(message.IndexOf("switch2", StringComparison.Ordinal));
        message.ShouldContain("--pipeline");
    }

    /// <summary>
    /// The three reserved names are not pipelines.
    /// </summary>
    /// <remarks>
    /// Every workspace in this repository, and every fixture, holds some of
    /// these three beside each other at the root, and all three match
    /// <c>preflight.*.json</c>. A naive enumeration turns all of them into an
    /// ambiguous selection at once — which is the single most likely way this
    /// change breaks everything simultaneously.
    /// </remarks>
    [Fact]
    public void Select_WithOnlyTheThreeReservedFilesPresent_IsNoneRatherThanAmbiguous()
    {
        Write("preflight.base.json", """{ "schemaVersion": 1 }""");
        Write("preflight.local.json", """{ "schemaVersion": 1 }""");
        Write("preflight.workspace.json", """{ "tools": [] }""");

        var selection = Select();

        selection.Pipeline.ShouldBeNull();
        selection.Source.ShouldBe(PipelineSource.None);
        PipelineSelector.Candidates(_workspace, _fileSystem).ShouldBeEmpty();
    }

    /// <summary>
    /// One candidate is still not a choice anybody made.
    /// </summary>
    /// <remarks>
    /// Adopting it would be convenient on the day there is one pipeline and a
    /// trap on the day a second appears, because the run would silently change
    /// what it validates. ADR-029 says declared, not inferred, and a single
    /// candidate is still an inference.
    /// </remarks>
    [Fact]
    public void Select_WithExactlyOneCandidateAndNoKey_IsNoneRatherThanAdoptingIt()
    {
        Write("preflight.base.json", """{ "schemaVersion": 1 }""");
        Write("preflight.atlas.json", """{ "schemaVersion": 1 }""");

        Select().Source.ShouldBe(PipelineSource.None);
    }

    [Fact]
    public void Select_WithNoPolicyFilesAtAll_IsNone()
    {
        var selection = Select();

        selection.Pipeline.ShouldBeNull();
        selection.Source.ShouldBe(PipelineSource.None);
    }

    /// <summary>
    /// The checkout key is validated as a label, exactly as the flag is.
    /// </summary>
    /// <remarks>
    /// The name becomes part of a filename, and this one arrives from a
    /// versioned file rather than from the person at the keyboard — a worse
    /// surface than the flag, not a better one, because nobody typed it today.
    /// </remarks>
    [Theory]
    [InlineData("../evil")]
    [InlineData("")]
    [InlineData("a b")]
    public void Select_WithACheckoutKeyThatIsNotALabel_ThrowsNamingTheFile(string pipeline)
    {
        Write("preflight.base.json", $$"""{ "schemaVersion": 1, "pipeline": "{{pipeline}}" }""");

        var exception = Should.Throw<PolicyValidationException>(() => Select());

        exception.Message.ShouldContain(PolicyResolution.BaseFileName);
    }

    /// <remarks>
    /// A pipeline named in both spellings never reaches policy validation when
    /// the selected pipeline document does not extend the base, so the refusal
    /// has to happen where the key is first read.
    /// </remarks>
    [Fact]
    public void Select_WithBothSpellingsOfTheKeyInBase_ThrowsNamingBoth()
    {
        Write("preflight.base.json", """
            { "schemaVersion": 1, "pipeline": "atlas", "production": "nova" }
            """);

        var exception = Should.Throw<PolicyValidationException>(() => Select());

        exception.Message.ShouldContain("pipeline");
        exception.Message.ShouldContain("production");
    }

    /// <summary>
    /// A file that matches the name pattern and is not a policy is not a
    /// candidate.
    /// </summary>
    /// <remarks>
    /// <c>preflight.deps.json</c> and <c>preflight.runtimeconfig.json</c> are
    /// emitted by the .NET build beside the executable and match
    /// <c>preflight.*.json</c> exactly. A name-only rule turns any directory
    /// holding them into an ambiguous selection, which is how this refusal
    /// would have fired on workspaces that own no pipeline at all. Extending
    /// the reserved list instead would be a list that grows with whatever the
    /// runtime emits next; what a pipeline file <em>is</em> is a policy
    /// document, and that is what gets checked.
    /// </remarks>
    [Theory]
    [InlineData("preflight.deps.json", """{ "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0" } }""")]
    [InlineData("preflight.runtimeconfig.json", """{ "runtimeOptions": { "tfm": "net10.0" } }""")]
    [InlineData("preflight.notes.json", """[1, 2, 3]""")]
    [InlineData("preflight.broken.json", """{ not json at all """)]
    [InlineData("preflight.manifestlike.json", """{ "tools": [ { "name": "git" } ] }""")]
    public void Candidates_IgnoresAFileThatIsNotAPolicyDocument(string name, string content)
    {
        Write("preflight.base.json", """{ "schemaVersion": 1 }""");
        Write("preflight.atlas.json", """{ "schemaVersion": 1 }""");
        Write(name, content);

        PipelineSelector.Candidates(_workspace, _fileSystem).ShouldBe(["preflight.atlas.json"]);
    }

    /// <remarks>
    /// The companion to the theory above: two real policy documents still
    /// refuse, so skipping non-policies did not quietly disable the refusal.
    /// </remarks>
    [Fact]
    public void Candidates_WithTwoRealPolicyDocuments_ListsBoth()
    {
        Write("preflight.base.json", """{ "schemaVersion": 1 }""");
        Write("preflight.atlas.json", """{ "schemaVersion": 1 }""");
        Write("preflight.switch2.json", """{ "schemaVersion": 1 }""");
        Write("preflight.deps.json", """{ "runtimeTarget": { "name": "x" } }""");

        PipelineSelector.Candidates(_workspace, _fileSystem)
            .ShouldBe(["preflight.atlas.json", "preflight.switch2.json"]);
    }

    [Fact]
    public void Candidates_IgnoresSubdirectories()
    {
        Directory.CreateDirectory(Path.Combine(_workspace.FullName, "sub"));
        File.WriteAllText(
            Path.Combine(_workspace.FullName, "sub", "preflight.other.json"), """{ "schemaVersion": 1 }""");

        PipelineSelector.Candidates(_workspace, _fileSystem).ShouldBeEmpty();
    }
}
