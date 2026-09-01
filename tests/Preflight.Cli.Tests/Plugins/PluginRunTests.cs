namespace Preflight.Cli.Tests.Plugins;

using System.Buffers.Binary;
using Preflight.Cli.Commands;
using Preflight.Cli.Tests.Commands;
using Preflight.Core;
using Preflight.TestSupport;

/// <summary>
/// A plugin rule executing, against a real repository.
/// </summary>
/// <remarks>
/// <para>
/// The assertion every other test around it stops short of. A loader that opens
/// an assembly, discovers a rule, lists it in <c>preflight rules</c> and never
/// runs it would leave all of them green — and would be a plugin model that
/// does nothing.
/// </para>
/// <para>
/// It needs a real repository because the sample is a pre-submit rule, and a
/// pre-submit run reads its changed files from git. The parser already refuses
/// <c>--stage pre-submit</c> without a ref, so there is no shortcut: this test
/// pays for a real repository, because faking it would mean faking the very
/// thing it is meant to prove. It follows the precedent of
/// <c>GitChangeSourceIntegrationTests</c>, including its cleanup, because git
/// marks objects read-only and <c>Directory.Delete</c> refuses those.
/// </para>
/// </remarks>
public sealed class PluginRunTests : IDisposable
{
    private readonly DirectoryInfo _workspace = Directory.CreateTempSubdirectory("preflight-plugin-run-");
    private readonly DirectoryInfo _plugins = PluginFixtures.PluginDirectory();
    private readonly DirectoryInfo _executable = Directory.CreateTempSubdirectory("preflight-plugin-run-bin-");
    private readonly ProcessRunner _processes = new();
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public void Dispose()
    {
        Unprotect(_workspace);

        PluginFixtures.TryDelete(_workspace);
        PluginFixtures.TryDelete(_plugins);
        PluginFixtures.TryDelete(_executable);
        _output.Dispose();
        _error.Dispose();
    }

    /// <summary>
    /// An oversized texture, judged by a rule that ships in nobody's binary.
    /// </summary>
    /// <remarks>
    /// Exit 1 and not 2: the plugin loaded correctly and the code was rejected,
    /// which the exit-code contract says calls the commit's author rather than the tool's
    /// owner. Getting that backwards would be the defect the exit-code table
    /// exists to prevent, arriving through the newest door in the tool.
    /// </remarks>
    [Fact]
    public async Task Run_WithAPluginRuleAndAnOversizedTexture_IsBlockedAndNamesTheRule()
    {
        await GivenARepositoryStaging("art/atlas.png", Png(8192, 8192));

        Invoke("run", "--stage", "pre-submit", "--changed-from", "HEAD", "--rules-path", _plugins.FullName)
            .ShouldBe(1);

        _output.ToString().ShouldContain(PluginFixtures.SampleRuleId);
        _output.ToString().ShouldContain("8192x8192px");
    }

    /// <summary>
    /// The same run, with a texture inside the limit.
    /// </summary>
    /// <remarks>
    /// The control. Without it the test above would pass against a plugin rule
    /// that failed everything it was shown, and the report would be evidence of
    /// nothing.
    /// </remarks>
    [Fact]
    public async Task Run_WithAPluginRuleAndATextureWithinTheLimit_IsZero()
    {
        await GivenARepositoryStaging("art/atlas.png", Png(1024, 1024));

        Invoke("run", "--stage", "pre-submit", "--changed-from", "HEAD", "--rules-path", _plugins.FullName)
            .ShouldBe(0);
    }

    /// <summary>
    /// The production's own policy governs the plugin's rule.
    /// </summary>
    /// <remarks>
    /// The worked example's closing claim, end to end: another production sets a
    /// different limit in its own policy and uses the same DLL. Nothing in the
    /// rule knows which production it is running for, and nothing in the engine
    /// treats a plugin's settings differently from a built-in's.
    /// </remarks>
    [Fact]
    public async Task Run_WithAPolicyTighteningThePluginRule_UsesThePipelinesLimit()
    {
        File.WriteAllText(
            Path.Combine(_workspace.FullName, "preflight.base.json"),
            $$"""
            {
              "schemaVersion": 1,
              "rules": { "{{PluginFixtures.SampleRuleId}}": { "settings": { "maxDimension": 512 } } }
            }
            """);

        await GivenARepositoryStaging("art/atlas.png", Png(1024, 1024));

        Invoke("run", "--stage", "pre-submit", "--changed-from", "HEAD", "--rules-path", _plugins.FullName)
            .ShouldBe(1);

        _output.ToString().ShouldContain("<= 512px");
    }

    /// <summary>
    /// A repository with one commit behind it and one file staged on top.
    /// </summary>
    /// <remarks>
    /// Staged rather than committed, so the diff against <c>HEAD</c> reports it.
    /// An untracked file appears in no <c>git diff</c> at all, which would make
    /// every rule here report <c>NotApplicable</c> and the run go green having
    /// examined nothing — success reported over a check that never ran, dressed
    /// as a passing test.
    /// </remarks>
    private async Task GivenARepositoryStaging(string relativePath, byte[] content)
    {
        // Every one of these four is pinned rather than inherited, and the
        // machine that would otherwise supply them is the one nobody can
        // inspect. A CI agent has no global identity, no init.defaultBranch and
        // no signing key; a developer machine may have all three, and one of
        // them configured differently is a failure that reads as "it works on
        // mine". GitChangeSourceIntegrationTests initialises its repository
        // under exactly this contract, and the two are meant to stay identical.
        await Git("init", "-b", "main");
        await Git("config", "user.email", "preflight@example.invalid");
        await Git("config", "user.name", "Preflight Tests");
        await Git("config", "commit.gpgsign", "false");

        File.WriteAllText(Path.Combine(_workspace.FullName, "README.md"), "fixture");

        await Git("add", ".");
        await Git("commit", "-m", "initial");

        var path = Path.Combine(_workspace.FullName, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);

        await Git("add", ".");
    }

    private async Task Git(params string[] arguments)
    {
        var result = await _processes.RunAsync(
            new Preflight.Abstractions.Services.ProcessRequest
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = _workspace.FullName,
            },
            TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(
            0,
            $"git {string.Join(' ', arguments)} failed: {result.StandardError}");
    }

    /// <summary>
    /// The first 24 bytes of a PNG: signature, chunk length, <c>IHDR</c>, width,
    /// height.
    /// </summary>
    private static byte[] Png(int width, int height)
    {
        var bytes = new byte[24];

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        signature.CopyTo(bytes);
        System.Text.Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes, 12);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);

        return bytes;
    }

    /// <remarks>
    /// git marks objects in <c>.git</c> read-only, and
    /// <see cref="Directory.Delete(string, bool)"/> refuses those.
    /// </remarks>
    private static void Unprotect(DirectoryInfo directory)
    {
        try
        {
            foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private int Invoke(params string[] args) => PreflightCommandLine.Execute(
        args,
        _output,
        _error,
        parse => PreflightCommandLine.Run(parse, CommandEnvironments.For(
            _workspace,
            _output,
            _error,
            TimeProvider.System,
            executableDirectory: _executable)));
}
