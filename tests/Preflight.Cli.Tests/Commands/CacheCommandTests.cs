namespace Preflight.Cli.Tests.Commands;

using Preflight.Core.Caching;
using Preflight.TestSupport;

/// <summary>
/// <c>preflight run</c> against a real cache, and <c>preflight cache clear</c>.
/// </summary>
/// <remarks>
/// The end-to-end half of the incremental cache. The engine's tests prove the cache is
/// consulted; these prove the CLI wires it to the directory policy names, that
/// <c>--no-cache</c> reaches it, and that clearing it is a command rather than
/// an instruction to delete a folder by hand.
/// </remarks>
public sealed class CacheCommandTests : IDisposable
{
    private readonly DirectoryInfo _workspace = Directory.CreateTempSubdirectory("preflight-cli-cache-");
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    private readonly FixedTimeProvider _clock =
        new(new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero));

    public CacheCommandTests()
    {
        // The probe declares its inputs, which is what makes it cacheable at
        // all. Without the declaration the fingerprint contract requires the fingerprint to
        // be null, and the whole feature is correctly invisible.
        Write("preflight.workspace.json", """
            {
              "tools": [
                { "name": "git", "command": "git", "arguments": ["--version"], "minimumVersion": "2.0.0" }
              ],
              "compileProbe": {
                "command": "git",
                "arguments": ["--version"],
                "inputs": ["src"]
              }
            }
            """);

        Write("src/a.c", "int main(){return 0;}");
        Write("config/build/any.json", """{ "contentRoot": "content" }""");
        Write("content/keep.txt", "x");
    }

    public void Dispose()
    {
        _workspace.Delete(recursive: true);
        _output.Dispose();
        _error.Dispose();
    }

    [Fact]
    public void Run_TwiceOverAnUnchangedWorkspace_ReportsTheSecondProbeAsCached()
    {
        Invoke("run", "--stage", "build-readiness").ShouldBe(0);
        _output.ToString().ShouldNotContain("(cached)");

        _output.GetStringBuilder().Clear();

        Invoke("run", "--stage", "build-readiness").ShouldBe(0);
        _output.ToString().ShouldContain("(cached)");
    }

    /// <remarks>
    /// The invalidation the whole design turns on. A fingerprint that failed to
    /// notice would report a <c>Passed</c> over a workspace that changed.
    /// </remarks>
    [Fact]
    public void Run_AfterADeclaredInputChanges_ExecutesTheProbeAgain()
    {
        Invoke("run", "--stage", "build-readiness").ShouldBe(0);

        Write("src/a.c", "int main(){return 1;}");
        _output.GetStringBuilder().Clear();

        Invoke("run", "--stage", "build-readiness").ShouldBe(0);

        _output.ToString().ShouldNotContain("(cached)");
    }

    [Fact]
    public void Run_WithNoCache_IgnoresWhatIsStored()
    {
        Invoke("run", "--stage", "build-readiness").ShouldBe(0);

        _output.GetStringBuilder().Clear();

        Invoke("run", "--stage", "build-readiness", "--no-cache").ShouldBe(0);

        _output.ToString().ShouldNotContain("(cached)");
    }

    /// <remarks>
    /// The policy schema lists <c>cachePath</c> beside <c>historyPath</c>, and the two
    /// behaving differently would be a trap for whoever pointed one of them at a
    /// share.
    /// </remarks>
    [Fact]
    public void Run_WritesToTheCachePathThePolicyNames()
    {
        Write("preflight.base.json", """{ "schemaVersion": 1, "cachePath": "build/cache" }""");

        Invoke("run", "--stage", "build-readiness").ShouldBe(0);

        Directory
            .GetFiles(Path.Combine(_workspace.FullName, "build", "cache"), "*.json", SearchOption.AllDirectories)
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void CacheClear_EmptiesTheCacheAndSaysHowMuchItRemoved()
    {
        Invoke("run", "--stage", "build-readiness").ShouldBe(0);

        _output.GetStringBuilder().Clear();

        Invoke("cache", "clear").ShouldBe(0);

        _output.ToString().ShouldContain("Removed 1 cached result from");

        Directory
            .GetFiles(CacheDirectory(), "*.json", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public void CacheClear_OverAWorkspaceThatWasNeverRun_IsZeroAndSucceeds()
    {
        Invoke("cache", "clear").ShouldBe(0);

        _output.ToString().ShouldContain("Removed 0 cached results from");
    }

    /// <remarks>
    /// The same rule applied to a third command: one regime for when the CLI
    /// accepts broken configuration, not one per command. Emptying a directory
    /// chosen by guesswork is the failure this prevents.
    /// </remarks>
    [Fact]
    public void CacheClear_OverAnInvalidPolicy_IsTwo()
    {
        Write("preflight.base.json", """{ "schemaVersion": 1, "cachePath": 42 }""");

        Invoke("cache", "clear").ShouldBe(2);
    }

    /// <remarks>
    /// A rule that does not implement <c>ICacheableRule</c> is never cached, and
    /// a probe with no declared inputs declines. Between them, an ordinary
    /// workspace stores nothing at all — which is what makes the feature safe by
    /// default.
    /// </remarks>
    [Fact]
    public void Run_ForAWorkspaceThatDeclaresNoProbeInputs_CachesNothing()
    {
        Write("preflight.workspace.json", """
            {
              "tools": [],
              "compileProbe": { "command": "git", "arguments": ["--version"] }
            }
            """);

        Invoke("run", "--stage", "build-readiness").ShouldBe(0);

        Directory.Exists(CacheDirectory()).ShouldBeFalse();
    }

    /// <summary>
    /// A cache that will not answer changes nothing but the speed.
    /// </summary>
    /// <remarks>
    /// The exit code and standard output are compared against the same run with
    /// a working cache, byte for byte. Asserting only the exit code would pass
    /// against an implementation that swallowed the failure and also swallowed
    /// half the report. The history format states this subordination for the history;
    /// it is truer still for an optimisation.
    /// </remarks>
    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void Run_WhenTheCacheWillNotAnswer_KeepsTheExitCodeAndTheReportBytes(Type failure)
    {
        var expectedCode = Invoke("run", "--stage", "build-readiness");
        var expectedReport = _output.ToString();

        _output.GetStringBuilder().Clear();
        _error.GetStringBuilder().Clear();

        Invoke(
            new FailingRuleCacheStore((Exception)Activator.CreateInstance(failure)!),
            "run",
            "--stage",
            "build-readiness")
            .ShouldBe(expectedCode);

        _output.ToString().ShouldBe(expectedReport);
    }

    /// <remarks>
    /// A cache path that contains the workspace is refused rather than emptied.
    /// Exit 2, through the same catch every other configuration error uses.
    /// </remarks>
    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void CacheClear_WhenTheCachePathContainsTheWorkspace_IsTwoAndDeletesNothing(string path)
    {
        Write("preflight.base.json", $$"""{ "schemaVersion": 1, "cachePath": "{{path}}" }""");

        Invoke("cache", "clear").ShouldBe(2);

        File.Exists(Path.Combine(_workspace.FullName, "preflight.workspace.json")).ShouldBeTrue();
        _error.ToString().ShouldContain("cachePath");
    }

    private string CacheDirectory() => Path.Combine(_workspace.FullName, ".preflight", "cache");

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_workspace.FullName, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private int Invoke(params string[] args) => Invoke(null, args);

    private int Invoke(IRuleCacheStore? cache, params string[] args) => PreflightCommandLine.Execute(
        args,
        _output,
        _error,
        parse => PreflightCommandLine.Run(
            parse,
            CommandEnvironments.For(_workspace, _output, _error, _clock, cache: cache)));
}
