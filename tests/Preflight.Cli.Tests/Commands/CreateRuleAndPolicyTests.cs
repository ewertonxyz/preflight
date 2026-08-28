namespace Preflight.Cli.Tests.Commands;

using System.Xml.Linq;
using NSubstitute;
using Preflight.Abstractions;
using Preflight.Cli.Commands;
using Preflight.Core;
using Preflight.Core.Policy;

/// <summary>
/// Fixes what <c>preflight create rule</c> and <c>preflight create policy</c>
/// write, and what they refuse.
/// </summary>
/// <remarks>
/// Both are scaffolds, and both are held to the promise <c>create workspace</c>
/// already makes: never replace a file, and translate a failed write into exit
/// 2 rather than letting an <see cref="IOException"/> reach the top as 3. The
/// refusals are asserted by non-invocation of the writer, because a write that
/// happened to restore the original bytes would satisfy a comparison and still
/// be the defect. See ADR-028.
/// </remarks>
public sealed class CreateRuleAndPolicyTests : IDisposable
{
    private readonly DirectoryInfo _workspace = Directory.CreateTempSubdirectory("preflight-scaffold-");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly IWorkspaceFileWriter _writer = Substitute.For<IWorkspaceFileWriter>();

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _workspace.Delete(recursive: true);
    }

    private CommandEnvironment Environment() => CommandEnvironments.For(
        _workspace, _output, _error, TimeProvider.System, workspaceWriter: _writer);

    private Task<int> Rule(string id) =>
        CreateCommandHandler.RuleAsync(Environment(), id, TestContext.Current.CancellationToken);

    private Task<int> Policy(string name) =>
        CreateCommandHandler.PolicyAsync(Environment(), name, TestContext.Current.CancellationToken);

    private Dictionary<string, string> Capture()
    {
        var written = new Dictionary<string, string>(StringComparer.Ordinal);

        _writer.Exists(Arg.Any<string>()).Returns(false);
        _writer
            .When(writer => writer.WriteNewAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(call => written[Path.GetFileName(call.ArgAt<string>(0))] = call.ArgAt<string>(1));

        return written;
    }

    /// <summary>
    /// The generated project references the contracts, and does not carry them.
    /// </summary>
    /// <remarks>
    /// <c>Private="false"</c> is the line the whole plugin model rests on, and
    /// it is the one a plugin author gets wrong by doing nothing at all: the
    /// default copies <c>Preflight.Abstractions.dll</c> into the output, the
    /// plugin ships its own copy of the contract, and the load context finds it
    /// sitting beside the plugin. The mirror of the assertion
    /// <c>SampleDependencyTests</c> already makes about the worked example — if
    /// the scaffold is wrong, every plugin written from it inherits the bug.
    /// </remarks>
    [Fact]
    public async Task RuleAsync_GeneratesAProjectWhoseReferenceIsNotPrivate()
    {
        var written = Capture();

        (await Rule("acme.textures.dimension")).ShouldBe(0);

        var csproj = XDocument.Parse(written["Acme.Textures.Dimension.csproj"]);

        csproj.Descendants("PackageReference").ShouldBeEmpty();

        var reference = csproj.Descendants("ProjectReference").ShouldHaveSingleItem();

        Path.GetFileNameWithoutExtension(((string)reference.Attribute("Include")!).Replace('\\', '/'))
            .ShouldBe("Preflight.Abstractions");

        ((string?)reference.Attribute("Private")).ShouldBe("false");
    }

    [Fact]
    public async Task RuleAsync_GeneratesASourceFileCarryingTheIdItWasGiven()
    {
        var written = Capture();

        (await Rule("acme.textures.dimension")).ShouldBe(0);

        written.Keys.ShouldBe(
            ["Acme.Textures.Dimension.csproj", "DimensionRule.cs"], ignoreOrder: true);

        written["DimensionRule.cs"].ShouldContain("acme.textures.dimension");
        written["DimensionRule.cs"].ShouldContain("class DimensionRule");
    }

    /// <summary>
    /// The generated source names members the contract actually has.
    /// </summary>
    /// <remarks>
    /// Nothing compiles this file — it is written into somebody else's project —
    /// so the ordinary guard against a renamed member does not apply to it. A
    /// scaffold naming a property that no longer exists is documentation that
    /// does not build, discovered by the one person least able to tell whether
    /// the mistake is theirs. Reflection over the contract is the cheapest thing
    /// that breaks here instead of there.
    /// </remarks>
    [Fact]
    public async Task RuleAsync_TheGeneratedSource_NamesOnlyMembersTheContractStillHas()
    {
        var written = Capture();

        (await Rule("acme.textures.dimension")).ShouldBe(0);

        var source = written["DimensionRule.cs"];
        var initialiser = source
            .Split("Descriptor { get; } = new()", StringSplitOptions.None)[1]
            .Split("};", StringSplitOptions.None)[0];

        var assigned = initialiser
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal))
            .Select(line => line.Split('=', 2)[0].Trim())
            .Where(name => name.Length > 0 && name.All(char.IsLetter))
            .ToArray();

        assigned.ShouldNotBeEmpty();

        foreach (var name in assigned)
        {
            typeof(RuleDescriptor).GetProperty(name).ShouldNotBeNull(
                $"The scaffold assigns RuleDescriptor.{name}, which no longer exists.");
        }

        var execute = typeof(IValidationRule).GetMethod(nameof(IValidationRule.ExecuteAsync))!;

        source.ShouldContain(
            $"Task<{execute.ReturnType.GetGenericArguments()[0].Name}> {execute.Name}(");
    }

    /// <remarks>
    /// <c>RuleId</c> throws <see cref="ArgumentException"/>, which is not a
    /// <c>ConfigurationLoadException</c> and therefore reaches the top as exit
    /// 3 — the code that says this tool broke, and sends the wrong person to
    /// look at a typo somebody made on the command line.
    /// </remarks>
    [Theory]
    [InlineData("Acme.Textures.Dimension")]
    [InlineData("acme.textures")]
    [InlineData("acme")]
    [InlineData("")]
    [InlineData("acme..dimension")]
    public async Task RuleAsync_ForAnIdThatIsNotARuleId_IsTwoNotThree(string id)
    {
        _writer.Exists(Arg.Any<string>()).Returns(false);

        var exception = await Should.ThrowAsync<WorkspaceFileExistsException>(() => Rule(id));

        ExitCode.ForException(exception).ShouldBe(ExitCode.ConfigurationError);

        // The framework's "(Parameter 'value')" is a fact about a method
        // signature in this repository, and it means nothing to somebody who
        // mistyped an id on the command line.
        exception.Message.ShouldNotContain("Parameter");

        await _writer.DidNotReceiveWithAnyArgs()
            .WriteNewAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    /// <remarks>
    /// <c>create policy base</c> would write <c>preflight.base.json</c>, which
    /// is not a pipeline overlay at all — it is the file the chain starts from,
    /// and the file <c>pipeline declare</c> owns. The three reserved names are
    /// the same three the pipeline selector already refuses to treat as
    /// pipelines, and the comparison ignores case for the reason that list
    /// gives.
    /// </remarks>
    [Theory]
    [InlineData("base")]
    [InlineData("local")]
    [InlineData("workspace")]
    [InlineData("Base")]
    [InlineData("LOCAL")]
    [InlineData("Workspace")]
    public async Task PolicyAsync_ForAReservedName_Refuses(string name)
    {
        _writer.Exists(Arg.Any<string>()).Returns(false);

        var exception = await Should.ThrowAsync<WorkspaceFileExistsException>(() => Policy(name));

        exception.Message.ShouldContain(name);

        await _writer.DidNotReceiveWithAnyArgs()
            .WriteNewAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PolicyAsync_ForANameThatIsNotALabel_Refuses()
    {
        _writer.Exists(Arg.Any<string>()).Returns(false);

        await Should.ThrowAsync<PolicyValidationException>(() => Policy("../evil"));

        await _writer.DidNotReceiveWithAnyArgs()
            .WriteNewAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PolicyAsync_WritesTheOverlayForTheNameItWasGiven()
    {
        var written = Capture();

        (await Policy("projecta")).ShouldBe(0);

        written.Keys.ShouldBe(["preflight.projecta.json"]);
        written["preflight.projecta.json"].ShouldContain("\"schemaVersion\": 1");
    }

    [Fact]
    public async Task PolicyAsync_TheGeneratedSkeleton_LoadsAsAPolicyDocument()
    {
        var written = Capture();

        (await Policy("projecta")).ShouldBe(0);

        var path = Path.Combine(_workspace.FullName, "preflight.projecta.json");

        await File.WriteAllTextAsync(
            path, written["preflight.projecta.json"], TestContext.Current.CancellationToken);

        var loaded = await new PolicyLoader(new PhysicalFileSystem())
            .LoadAsync(path, TestContext.Current.CancellationToken);

        loaded.Document.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RuleAsync_WhenEitherTargetExists_RefusesAndWritesNothing(
        bool projectExists, bool sourceExists)
    {
        _writer.Exists(Arg.Any<string>()).Returns(call =>
            call.ArgAt<string>(0).EndsWith(".csproj", StringComparison.Ordinal)
                ? projectExists
                : sourceExists);

        await Should.ThrowAsync<WorkspaceFileExistsException>(() => Rule("acme.textures.dimension"));

        await _writer.DidNotReceiveWithAnyArgs()
            .WriteNewAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PolicyAsync_WhenTheTargetExists_RefusesAndWritesNothing()
    {
        _writer.Exists(Arg.Any<string>()).Returns(true);

        var exception = await Should.ThrowAsync<WorkspaceFileExistsException>(() => Policy("projecta"));

        exception.Message.ShouldContain("preflight.projecta.json");

        await _writer.DidNotReceiveWithAnyArgs()
            .WriteNewAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenTheWriterThrows_IsTwoNotThree(bool rule)
    {
        _writer.Exists(Arg.Any<string>()).Returns(false);
        _writer.WriteNewAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new UnauthorizedAccessException("read-only"));

        var exception = await Should.ThrowAsync<WorkspaceFileExistsException>(
            () => rule ? Rule("acme.textures.dimension") : Policy("projecta"));

        exception.Message.ShouldContain("read-only");
        ExitCode.ForException(exception).ShouldBe(ExitCode.ConfigurationError);
        _output.ToString().ShouldNotContain("Wrote");
    }
}
