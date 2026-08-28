namespace Preflight.Core.Tests.Policy;

using NSubstitute;
using Preflight.Abstractions.Services;
using Preflight.Core.Policy;

/// <summary>
/// Fixes <see cref="PolicyLoader"/>'s resolution of the <c>extends</c> chain:
/// cycle detection with the full ordered chain, missing-target reporting with
/// an absolute path, and resolution relative to the declaring file.
/// </summary>
/// <remarks>
/// policy precedence and 6.4. The loader reads through
/// <see cref="IFileSystem"/> rather than the disk directly, so these tests
/// never touch a real file — consistent with the fakes-only testing philosophy
/// already used for the built-in rules (the test strategy).
/// </remarks>
public sealed class PolicyLoaderTests
{
    [Fact]
    public async Task LoadAsync_WithNoExtends_ReturnsTheDocumentUnmerged()
    {
        var fileSystem = FileSystemWith(("C:\\repo\\base.json", """{ "schemaVersion": 1, "production": "base" }"""));

        var document = (await new PolicyLoader(fileSystem).LoadAsync("C:\\repo\\base.json", CancellationToken.None)).Document;

        document.TryGetRaw("production", out var production).ShouldBeTrue();
        production.ShouldBe("base");
    }

    [Fact]
    public async Task LoadAsync_WithASingleExtendsHop_MergesChildOverBase_ChildWins()
    {
        var fileSystem = FileSystemWith(
            ("C:\\repo\\base.json", """{ "schemaVersion": 1, "rules": { "core.a.b": { "enabled": true, "blocking": true } } }"""),
            ("C:\\repo\\atlas.json", """{ "schemaVersion": 1, "extends": "base.json", "rules": { "core.a.b": { "blocking": false } } }"""));

        var document = (await new PolicyLoader(fileSystem).LoadAsync("C:\\repo\\atlas.json", CancellationToken.None)).Document;

        document.TryGetRaw(["rules", "core.a.b", "enabled"], out var enabled).ShouldBeTrue();
        enabled.ShouldBe(true);
        document.TryGetRaw(["rules", "core.a.b", "blocking"], out var blocking).ShouldBeTrue();
        blocking.ShouldBe(false);
    }

    [Fact]
    public async Task LoadAsync_WithATwoHopChain_MergesInPrecedenceOrder_NearestFileWinsOverFurthestAncestor()
    {
        var fileSystem = FileSystemWith(
            ("C:\\repo\\c.json", """{ "schemaVersion": 1, "maxDegreeOfParallelism": 1 }"""),
            ("C:\\repo\\b.json", """{ "schemaVersion": 1, "extends": "c.json", "maxDegreeOfParallelism": 2 }"""),
            ("C:\\repo\\a.json", """{ "schemaVersion": 1, "extends": "b.json" }"""));

        var document = (await new PolicyLoader(fileSystem).LoadAsync("C:\\repo\\a.json", CancellationToken.None)).Document;

        document.TryGetRaw("maxDegreeOfParallelism", out var value).ShouldBeTrue();
        value.ShouldBe(2L);
    }

    [Fact]
    public async Task LoadAsync_WhenAFileExtendsItself_ThrowsPolicyValidationExceptionNamingTheOneFileChain()
    {
        var fileSystem = FileSystemWith(("C:\\repo\\a.json", """{ "schemaVersion": 1, "extends": "a.json" }"""));

        var exception = await Should.ThrowAsync<PolicyValidationException>(
            () => new PolicyLoader(fileSystem).LoadAsync("C:\\repo\\a.json", CancellationToken.None));

        exception.Errors.ShouldContain(error => error.Message.Contains("a.json"));
    }

    [Fact]
    public async Task LoadAsync_WhenExtendsFormsAnIndirectCycle_ThrowsPolicyValidationExceptionListingTheFullChainInOrder()
    {
        var fileSystem = FileSystemWith(
            ("C:\\repo\\a.json", """{ "schemaVersion": 1, "extends": "b.json" }"""),
            ("C:\\repo\\b.json", """{ "schemaVersion": 1, "extends": "a.json" }"""));

        var exception = await Should.ThrowAsync<PolicyValidationException>(
            () => new PolicyLoader(fileSystem).LoadAsync("C:\\repo\\a.json", CancellationToken.None));

        var message = exception.Errors[0].Message;
        message.IndexOf("a.json", StringComparison.Ordinal)
            .ShouldBeLessThan(message.LastIndexOf("b.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_WhenExtendsTargetDoesNotExist_ThrowsPolicyValidationExceptionWithTheResolvedAbsolutePath()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists("C:\\repo\\atlas.json").Returns(true);
        fileSystem.ReadAllTextAsync("C:\\repo\\atlas.json", Arg.Any<CancellationToken>())
            .Returns("""{ "schemaVersion": 1, "extends": "missing.json" }""");
        fileSystem.FileExists("C:\\repo\\missing.json").Returns(false);

        var exception = await Should.ThrowAsync<PolicyValidationException>(
            () => new PolicyLoader(fileSystem).LoadAsync("C:\\repo\\atlas.json", CancellationToken.None));

        exception.Errors.ShouldContain(error => error.Message.Contains("C:\\repo\\missing.json"));
    }

    [Fact]
    public async Task LoadAsync_ResolvesExtendsRelativeToTheDeclaringFilesDirectory_NotTheCurrentWorkingDirectory()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.FileExists("C:\\repo\\productions\\atlas\\preflight.atlas.json").Returns(true);
        fileSystem.ReadAllTextAsync("C:\\repo\\productions\\atlas\\preflight.atlas.json", Arg.Any<CancellationToken>())
            .Returns("""{ "schemaVersion": 1, "extends": "../base/preflight.base.json" }""");
        fileSystem.FileExists("C:\\repo\\productions\\base\\preflight.base.json").Returns(true);
        fileSystem.ReadAllTextAsync("C:\\repo\\productions\\base\\preflight.base.json", Arg.Any<CancellationToken>())
            .Returns("""{ "schemaVersion": 1, "production": "base" }""");

        var document = (await new PolicyLoader(fileSystem).LoadAsync(
            "C:\\repo\\productions\\atlas\\preflight.atlas.json", CancellationToken.None)).Document;

        document.TryGetRaw("production", out var production).ShouldBeTrue();
        production.ShouldBe("base");
    }

    /// <remarks>
    /// Policy validation lists only semantic problems, but a policy file can
    /// also simply be broken JSON. Letting a raw <c>JsonException</c> escape
    /// would give the tool a second, undocumented error shape; the loader wraps
    /// it so there is one surface.
    /// </remarks>
    [Fact]
    public async Task LoadAsync_WithMalformedJsonMidDocument_ThrowsPolicyValidationExceptionWithAOneBasedLineNumber()
    {
        var fileSystem = FileSystemWith(("C:\\repo\\atlas.json", "{\n  \"schemaVersion\": 1,\n  \"extends\": ,\n}"));

        var exception = await Should.ThrowAsync<PolicyValidationException>(
            () => new PolicyLoader(fileSystem).LoadAsync("C:\\repo\\atlas.json", CancellationToken.None));

        exception.Errors[0].Message.ShouldContain("Malformed JSON");
        exception.Errors[0].Line.ShouldNotBeNull();
    }

    [Fact]
    public async Task LoadAsync_WithAnEmptyPolicyFile_ThrowsPolicyValidationExceptionNamingTheFile()
    {
        var fileSystem = FileSystemWith(("C:\\repo\\atlas.json", string.Empty));

        var exception = await Should.ThrowAsync<PolicyValidationException>(
            () => new PolicyLoader(fileSystem).LoadAsync("C:\\repo\\atlas.json", CancellationToken.None));

        exception.Errors[0].Message.ShouldContain("Malformed JSON");
        exception.Errors[0].FilePath.ShouldBe("C:\\repo\\atlas.json");
    }

    /// <summary>
    /// The chain comes back furthest-ancestor-first, which is application
    /// order.
    /// </summary>
    /// <remarks>
    /// The order is the assertion, not the contents. The traversal builds the
    /// list entry-file-first, because that is the order a cycle has to be
    /// reported in (policy validation), and policy precedence, the explain
    /// command and the console report all want the opposite. A reversal that
    /// goes missing produces a header reading <c>atlas → base</c>, which states
    /// the precedence backwards while naming exactly the right files — the kind
    /// of wrong that survives review.
    /// </remarks>
    [Fact]
    public async Task LoadAsync_WithATwoHopChain_ReturnsTheChainInApplicationOrder_FurthestAncestorFirst()
    {
        var fileSystem = FileSystemWith(
            ("C:\\repo\\a.json", """{ "schemaVersion": 1, "extends": "b.json" }"""),
            ("C:\\repo\\b.json", """{ "schemaVersion": 1, "extends": "c.json" }"""),
            ("C:\\repo\\c.json", """{ "schemaVersion": 1, "production": "base" }"""));

        var result = await new PolicyLoader(fileSystem).LoadAsync("C:\\repo\\a.json", CancellationToken.None);

        result.Chain.ShouldBe([
            "C:\\repo\\c.json",
            "C:\\repo\\b.json",
            "C:\\repo\\a.json",
        ]);
    }

    /// <remarks>
    /// A file with no <c>extends</c> yields a chain of one, never an empty one.
    /// The console header of the console report prints the chain
    /// unconditionally, and an empty list there would render as a blank
    /// <c>policy</c> line — a run that looks unconfigured while being
    /// configured.
    /// </remarks>
    [Fact]
    public async Task LoadAsync_WithNoExtends_ReturnsAChainOfTheEntryFileAlone()
    {
        var fileSystem = FileSystemWith(("C:\\repo\\base.json", """{ "schemaVersion": 1 }"""));

        var result = await new PolicyLoader(fileSystem).LoadAsync("C:\\repo\\base.json", CancellationToken.None);

        result.Chain.ShouldBe(["C:\\repo\\base.json"]);
    }

    private static IFileSystem FileSystemWith(params (string Path, string Json)[] files)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        foreach (var (path, json) in files)
        {
            fileSystem.FileExists(path).Returns(true);
            fileSystem.ReadAllTextAsync(path, Arg.Any<CancellationToken>()).Returns(json);
        }

        return fileSystem;
    }
}
