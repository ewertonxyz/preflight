namespace Preflight.Rules.Tests.Build;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core;
using Preflight.Core.Execution;

/// <summary>
/// The fingerprint of the one rule the cache exists for.
/// </summary>
/// <remarks>
/// <para>
/// On a real disk, because the whole question is what the bytes on disk say,
/// and a fingerprint checked against a substituted file system asserts only
/// that the substitute was configured to agree with it. That is the argument
/// for having an integration layer at all: a unit test proves the code does
/// what it was told, and this proves it was told the right thing.
/// </para>
/// <para>
/// Every "changes" test below is guarding the expensive direction. A
/// fingerprint that fails to change does not fail here in production — it
/// serves a <c>Passed</c> over a workspace that changed, and the evidence of
/// the mistake is the run that did not happen.
/// </para>
/// </remarks>
public sealed class CompileProbeFingerprintTests : IDisposable
{
    private readonly DirectoryInfo _workspace = Directory.CreateTempSubdirectory("preflight-fingerprint-");

    public void Dispose() => _workspace.Delete(recursive: true);

    /// <summary>
    /// Without a declaration there is no fingerprint, and that is the default.
    /// </summary>
    /// <remarks>
    /// There is no approximate fingerprint, and the engine cannot work out what
    /// a compiler reads. A workspace that has not said what its probe reads
    /// gets the safe answer, which is no caching at all.
    /// </remarks>
    [Fact]
    public async Task ComputeFingerprintAsync_WithNoManifest_IsNull() =>
        (await Fingerprint()).ShouldBeNull();

    [Fact]
    public async Task ComputeFingerprintAsync_WithNoProbeDeclared_IsNull()
    {
        Write("preflight.workspace.json", """{ "tools": [] }""");

        (await Fingerprint()).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(""", "inputs": []""")]
    public async Task ComputeFingerprintAsync_WithoutDeclaredInputs_IsNull(string inputs)
    {
        WriteManifest(inputs);

        (await Fingerprint()).ShouldBeNull();
    }

    /// <remarks>
    /// A manifest that will not parse is a <c>Failed</c> the rule itself
    /// reports. Throwing out of the fingerprint instead would make a syntax
    /// error in a JSON file surface as a cache defect.
    /// </remarks>
    [Fact]
    public async Task ComputeFingerprintAsync_WithAManifestThatWillNotParse_IsNullRatherThanAThrow()
    {
        Write("preflight.workspace.json", "{ not json");

        (await Fingerprint()).ShouldBeNull();
    }

    [Fact]
    public async Task ComputeFingerprintAsync_ForUnchangedInputs_IsStable()
    {
        WriteManifest(""", "inputs": ["src"]""");
        Write("src/a.c", "int main(){return 0;}");

        (await Fingerprint()).ShouldBe(await Fingerprint());
    }

    /// <remarks>
    /// Content, never a timestamp. An mtime-based fingerprint is wrong in both
    /// directions: a checkout restores content and changes every timestamp, and
    /// a file written twice in one tick changes content and keeps one.
    /// </remarks>
    [Fact]
    public async Task ComputeFingerprintAsync_WhenAFilesContentChanges_Changes()
    {
        WriteManifest(""", "inputs": ["src"]""");
        Write("src/a.c", "int main(){return 0;}");

        var before = await Fingerprint();

        Write("src/a.c", "int main(){return 1;}");

        (await Fingerprint()).ShouldNotBe(before);
    }

    [Fact]
    public async Task ComputeFingerprintAsync_WhenAFileIsAddedToADeclaredDirectory_Changes()
    {
        WriteManifest(""", "inputs": ["src"]""");
        Write("src/a.c", "int main(){return 0;}");

        var before = await Fingerprint();

        Write("src/b.c", "static void helper(void){}");

        (await Fingerprint()).ShouldNotBe(before);
    }

    /// <remarks>
    /// Changing a compiler flag changes the answer without changing one byte of
    /// the sources. It is the same class of mistake as leaving the effective
    /// policy out of the key.
    /// </remarks>
    [Fact]
    public async Task ComputeFingerprintAsync_WhenTheProbeCommandLineChanges_Changes()
    {
        WriteManifest(""", "inputs": ["src"]""");
        Write("src/a.c", "int main(){return 0;}");

        var before = await Fingerprint();

        WriteManifest(""", "inputs": ["src"]""", arguments: """["build", "--optimise"]""");

        (await Fingerprint()).ShouldNotBe(before);
    }

    /// <remarks>
    /// A path that is not there is described as absent rather than skipped. A
    /// directory that appears between two runs changes what the compiler sees,
    /// and a fingerprint that ignored its absence would keep serving the result
    /// from before it existed.
    /// </remarks>
    [Fact]
    public async Task ComputeFingerprintAsync_WhenADeclaredPathAppears_Changes()
    {
        WriteManifest(""", "inputs": ["src", "generated"]""");
        Write("src/a.c", "int main(){return 0;}");

        var before = await Fingerprint();

        Write("generated/version.h", "#define V 2");

        (await Fingerprint()).ShouldNotBe(before);
    }

    [Fact]
    public async Task ComputeFingerprintAsync_ForADeclaredFileRatherThanADirectory_TracksIt()
    {
        WriteManifest(""", "inputs": ["build.props"]""");
        Write("build.props", "<Project />");

        var before = await Fingerprint();

        Write("build.props", "<Project><PropertyGroup /></Project>");

        (await Fingerprint()).ShouldNotBe(before);
    }

    /// <remarks>
    /// Two workspaces holding the same bytes fingerprint alike even though they
    /// sit at different absolute paths. Without it the cache would miss on
    /// every machine but the one that filled it, and on every CI agent that
    /// checks out into a per-build directory — which is all of them.
    /// </remarks>
    [Fact]
    public async Task ComputeFingerprintAsync_ForTwoWorkspacesWithTheSameContent_IsTheSame()
    {
        WriteManifest(""", "inputs": ["src"]""");
        Write("src/a.c", "int main(){return 0;}");

        var here = await Fingerprint();

        var elsewhere = Directory.CreateTempSubdirectory("preflight-fingerprint-twin-");

        try
        {
            File.Copy(
                Path.Combine(_workspace.FullName, "preflight.workspace.json"),
                Path.Combine(elsewhere.FullName, "preflight.workspace.json"));

            Directory.CreateDirectory(Path.Combine(elsewhere.FullName, "src"));
            File.Copy(
                Path.Combine(_workspace.FullName, "src", "a.c"),
                Path.Combine(elsewhere.FullName, "src", "a.c"));

            (await Fingerprint(elsewhere)).ShouldBe(here);
        }
        finally
        {
            elsewhere.Delete(recursive: true);
        }
    }

    private void WriteManifest(string extra, string arguments = """["build"]""") =>
        Write("preflight.workspace.json", $$"""
            {
              "tools": [],
              "compileProbe": {
                "command": "cc",
                "arguments": {{arguments}}{{extra}}
              }
            }
            """);

    private void Write(string relativePath, string content) => Write(_workspace, relativePath, content);

    private static void Write(DirectoryInfo root, string relativePath, string content)
    {
        var path = Path.Combine(root.FullName, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task<CacheFingerprint?> Fingerprint(DirectoryInfo? workspace = null) =>
        await new CompileProbeRule().ComputeFingerprintAsync(
            RuleFixture.Context(
                fileSystem: new PhysicalFileSystem(),
                stage: ValidationStage.BuildReadiness,
                workspaceRoot: workspace ?? _workspace),
            TestContext.Current.CancellationToken);
}
