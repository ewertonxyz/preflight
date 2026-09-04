namespace Preflight.Specs.Steps;

using System.Diagnostics;
using System.Text;
using Preflight.TestSupport;
using Reqnroll;

/// <summary>
/// Drives the real executable over a workspace built for the scenario.
/// </summary>
/// <remarks>
/// <para>
/// The vocabulary is deliberately small: build a workspace, run the binary,
/// read the exit code and the report. Everything the scenarios distinguish —
/// which quadrant of the <c>blocking</c> × <c>gating</c> matrix, which rule was
/// blamed for a skip, which exit code a pipeline sees — comes out of those four
/// verbs, because a scenario that needed a fifth would be describing an
/// implementation rather than a rule of the system.
/// </para>
/// <para>
/// The rules are the six real ones. Shaping their behaviour through policy
/// rather than through fakes is what makes these scenarios worth having: a fake
/// rule proves the tool, and the tool already has unit tests. What has no
/// other test is whether the six shipped rules, the policy chain and the exit
/// codes agree with each other when a real process runs them.
/// </para>
/// </remarks>
[Binding]
public sealed class WorkspaceSteps : IDisposable
{
    private readonly DirectoryInfo _workspace =
        Directory.CreateTempSubdirectory("preflight-spec-");

    private readonly Dictionary<string, string> _environment = [];

    /// <summary>
    /// An install root holding nothing, shared by every scenario.
    /// </summary>
    /// <remarks>
    /// Created once and never installed into, on the same terms as the empty
    /// plugin directory: what it has to be is somewhere the machine's own
    /// installed pipelines are not.
    /// </remarks>
    private static readonly Lazy<DirectoryInfo> InstallRoot =
        new(() => Directory.CreateTempSubdirectory("preflight-specs-install-root-"));

    private readonly List<DirectoryInfo> _pluginDirectories = [];

    private readonly Dictionary<string, byte[]> _rememberedFiles = [];

    private int _exitCode = int.MinValue;
    private string _standardOutput = string.Empty;
    private string _standardError = string.Empty;

    public void Dispose()
    {
        try
        {
            _workspace.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that outlives the run is litter, not a failure.
        }

        // The same forgiveness, for the same reason and one more: an assembly
        // the child process loaded keeps its file open until that process ends,
        // and the scenario finishes first.
        foreach (var directory in _pluginDirectories)
        {
            PluginFixtures.TryDelete(directory);
        }
    }

    /// <remarks>
    /// The directory is created by the field initialiser; this step exists so a
    /// scenario reads as a sentence rather than starting mid-thought.
    /// </remarks>
    [Given("a workspace")]
    public static void GivenAWorkspace()
    {
    }

    /// <remarks>
    /// The toolchain rule is the only root of the workspace stage and the only
    /// gating rule in it, which makes it the lever every matrix scenario pulls:
    /// a version nothing satisfies fails it on demand, deterministically, with
    /// no compiler or SDK involved.
    /// </remarks>
    [Given("the workspace needs git {string} or newer")]
    public void GivenTheWorkspaceNeedsGit(string minimumVersion) =>
        Write("preflight.workspace.json", $$"""
            {
              "tools": [
                {
                  "name": "git",
                  "command": "git",
                  "arguments": ["--version"],
                  "minimumVersion": "{{minimumVersion}}"
                }
              ]
            }
            """);

    [Given("the workspace needs nothing")]
    public void GivenTheWorkspaceNeedsNothing() =>
        Write("preflight.workspace.json", """{ "tools": [] }""");

    /// <remarks>
    /// A dependency with a marker nothing satisfies. That is a warning rather
    /// than a failure, which is what gives these scenarios a real
    /// <c>PassedWithWarnings</c> to aggregate instead of a fabricated one.
    /// </remarks>
    [Given("the workspace declares a dependency that was never restored")]
    public void GivenAnUnrestoredDependency() =>
        Write("preflight.workspace.json", """
            {
              "tools": [
                { "name": "git", "command": "git", "arguments": ["--version"], "minimumVersion": "2.0.0" }
              ],
              "dependencies": [
                { "id": "Serilog", "version": "3.1.1", "restoredMarker": "packages/serilog" }
              ]
            }
            """);

    [Given("the file {string} contains")]
    public void GivenTheFileContains(string relativePath, string content) =>
        Write(relativePath, content);

    [Given("the environment variable {string} is {string}")]
    public void GivenTheEnvironmentVariableIs(string name, string value) =>
        _environment[name] = value;

    /// <summary>
    /// A directory of real plugin assemblies, built from the worked-example
    /// sample.
    /// </summary>
    /// <remarks>
    /// The real sample rather than a fake, for the reason this whole project
    /// runs the six real rules rather than doubles: a fake plugin proves the
    /// loader, and the loader already has unit tests. What has no other test is
    /// whether an assembly built the way a production would build one is found,
    /// loaded and honoured by a process started from a shell.
    /// </remarks>
    [Given("a plugin directory holding the sample rule")]
    public void GivenAPluginDirectoryHoldingTheSampleRule() =>
        _pluginDirectories.Add(PluginFixtures.PluginDirectory());

    /// <remarks>
    /// A second copy of the same plugin, which is the collision that actually
    /// happens — a production that deployed its rules twice, into a directory
    /// beside the tool and into one of its own.
    /// </remarks>
    [Given("a second plugin directory holding the sample rule")]
    public void GivenASecondPluginDirectoryHoldingTheSampleRule() =>
        GivenAPluginDirectoryHoldingTheSampleRule();

    [Given("a plugin directory holding a corrupt assembly")]
    public void GivenAPluginDirectoryHoldingACorruptAssembly() =>
        _pluginDirectories.Add(PluginFixtures.BrokenPluginDirectory());

    [When("preflight is invoked with {string}")]
    public void WhenPreflightIsInvokedWith(string arguments) =>
        Invoke(Split(arguments));

    /// <remarks>
    /// A step of its own rather than a path written into the scenario, because
    /// the directories are temporary and their names are decided at run time. A
    /// scenario naming one would either point at a fixture committed to this
    /// repository or at whatever the previous run left behind.
    /// </remarks>
    [When("preflight is invoked with {string} and the plugin directories")]
    public void WhenPreflightIsInvokedWithThePluginDirectories(string arguments)
    {
        var invocation = Split(arguments);

        foreach (var directory in _pluginDirectories)
        {
            invocation.Add("--rules-path");
            invocation.Add(directory.FullName);
        }

        Invoke(invocation);
    }

    /// <summary>
    /// Remembers a file's bytes so a later step can prove nothing touched them.
    /// </summary>
    /// <remarks>
    /// The bytes, not the timestamp: a write that produced identical content
    /// would still move the timestamp on some file systems and not on others,
    /// and the promise being checked is about the content.
    /// </remarks>
    [Given("the file {string} is remembered")]
    public void GivenTheFileIsRemembered(string relativePath) =>
        _rememberedFiles[relativePath] = File.ReadAllBytes(Path.Combine(_workspace.FullName, relativePath));

    [Then("the file {string} is unchanged")]
    public void ThenTheFileIsUnchanged(string relativePath)
    {
        var expected = _rememberedFiles[relativePath];
        var actual = File.ReadAllBytes(Path.Combine(_workspace.FullName, relativePath));

        actual.ShouldBe(expected, $"'{relativePath}' was rewritten.");
    }

    [Then("the error output does not say {string}")]
    public void ThenTheErrorOutputDoesNotSay(string unexpected) =>
        _standardError.ShouldNotContain(unexpected, customMessage: _standardError);

    [Then("it exits with code {int}")]
    public void ThenItExitsWithCode(int expected) =>
        _exitCode.ShouldBe(expected, $"stdout:\n{_standardOutput}\nstderr:\n{_standardError}");

    [Then("the report says {string}")]
    public void ThenTheReportSays(string expected) =>
        _standardOutput.ShouldContain(expected, customMessage: _standardOutput);

    [Then("the report does not say {string}")]
    public void ThenTheReportDoesNotSay(string unexpected) =>
        _standardOutput.ShouldNotContain(unexpected, customMessage: _standardOutput);

    /// <remarks>
    /// Order, not presence. The root cause reads before the symptom, and the
    /// fixed ordering is what buys that; a scenario asserting only that both
    /// appear would pass against a report that printed them backwards.
    /// </remarks>
    [Then("{string} is reported before {string}")]
    public void ThenIsReportedBefore(string first, string second)
    {
        var firstIndex = _standardOutput.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = _standardOutput.IndexOf(second, StringComparison.Ordinal);

        firstIndex.ShouldBeGreaterThanOrEqualTo(0, $"'{first}' is missing from:\n{_standardOutput}");
        secondIndex.ShouldBeGreaterThanOrEqualTo(0, $"'{second}' is missing from:\n{_standardOutput}");
        firstIndex.ShouldBeLessThan(secondIndex, _standardOutput);
    }

    /// <summary>
    /// The command surface, as an exact set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads standard output rather than discarding it. Without that, the
    /// no-arguments scenario asserts only that the process started, which stays
    /// green for a binary that prints nothing at all.
    /// </para>
    /// <para>
    /// A table rather than one placeholder per command, and an exact comparison
    /// rather than a containment check per row. The previous shape had exactly
    /// four placeholders and would have gone on passing with two commands
    /// missing. A scenario that claims to name the whole command surface is
    /// worth nothing unless it fails when the surface grows.
    /// </para>
    /// </remarks>
    [Then("the report names exactly these commands")]
    public void ThenTheReportNamesExactlyTheseCommands(DataTable commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var listed = _standardOutput
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("Commands:", StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => line.StartsWith("  ", StringComparison.Ordinal))
            .Select(line => line.Trim().Split(' ')[0])
            .ToArray();

        listed.ShouldBe(
            [.. commands.Rows.Select(row => row[0])],
            customMessage: _standardOutput);
    }

    /// <summary>
    /// How many records the history holds.
    /// </summary>
    /// <remarks>
    /// Counted across every file in the directory, because <c>historyMode</c>
    /// decides how many there are and a scenario should not have to know which
    /// mode it is in. A directory that was never created counts as nought: that
    /// is the state before the first write, and it is what the scenarios about
    /// commands that record nothing assert.
    /// </remarks>
    [Then("the history holds {int} record(s)")]
    public void ThenTheHistoryHolds(int expected)
    {
        var directory = Path.Combine(_workspace.FullName, ".preflight", "history");

        var records = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.ndjson")
                .OrderBy(path => path, StringComparer.Ordinal)
                .SelectMany(File.ReadAllLines)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray()
            : [];

        records.Length.ShouldBe(expected, string.Join("\n", records));
    }

    /// <summary>
    /// How many results the cache holds.
    /// </summary>
    /// <remarks>
    /// Counted across every rule's directory, because a scenario should not
    /// have to know which rules turned out to be cacheable. A cache directory
    /// that was never created counts as nought, which is the state a workspace
    /// that declared no probe inputs has to be in.
    /// </remarks>
    [Then("the cache holds {int} result(s)")]
    public void ThenTheCacheHolds(int expected)
    {
        var directory = Path.Combine(_workspace.FullName, ".preflight", "cache");

        var results = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories)
            : [];

        results.Length.ShouldBe(expected, string.Join("\n", results));
    }

    /// <remarks>
    /// A workspace the build-readiness stage can get all the way through: git
    /// is on every machine that can build this project, the configuration rule
    /// needs a content root that exists, and the probe is declared with the
    /// inputs that are required before anything may be cached.
    /// </remarks>
    [Given("the workspace's compile probe declares its inputs")]
    public void GivenTheProbeDeclaresItsInputs()
    {
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

    /// <remarks>
    /// The same workspace with the declaration removed. That is the safe
    /// default, and a scenario is the only place the two can be compared as one
    /// behaviour rather than two settings.
    /// </remarks>
    [Given("the workspace's compile probe declares nothing")]
    public void GivenTheProbeDeclaresNothing()
    {
        GivenTheProbeDeclaresItsInputs();

        Write("preflight.workspace.json", """
            {
              "tools": [
                { "name": "git", "command": "git", "arguments": ["--version"], "minimumVersion": "2.0.0" }
              ],
              "compileProbe": { "command": "git", "arguments": ["--version"] }
            }
            """);
    }

    [Then("the error output says {string}")]
    public void ThenTheErrorOutputSays(string expected) =>
        _standardError.ShouldContain(expected, customMessage: _standardError);

    /// <remarks>
    /// Quoted segments are kept whole, so a scenario can pass an argument with
    /// a space — a policy value, a ref name — without the step definition
    /// guessing where one argument ends.
    /// </remarks>
    private static List<string> Split(string arguments)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        foreach (var character in arguments)
        {
            if (character == '\'')
            {
                quoted = !quoted;

                continue;
            }

            if (character == ' ' && !quoted)
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_workspace.FullName, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void Invoke(IReadOnlyList<string> arguments)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "preflight.dll");

        File.Exists(executable).ShouldBeTrue($"Expected the CLI at {executable}");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,

            // Decoded as UTF-8 whatever the host's console is set to. Without
            // these two, the bytes the child wrote are decoded with the parent's
            // Console.OutputEncoding — a console code page on a developer
            // machine, UTF-8 with no console attached on a build agent — so the
            // same scenario could read differently in two places. It costs
            // nothing today, because no step asserts on a glyph and GlyphSet
            // falls back to ASCII when the encoding does not round-trip; it
            // would cost a day on the first scenario that asserts a checkmark.
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,

            // The workspace is the current directory, because that is how the
            // tool finds one. Passing a path would test a flag this scenario does
            // not have.
            WorkingDirectory = _workspace.FullName,
        };

        startInfo.ArgumentList.Add(executable);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Cleared first. A developer machine or a build agent may already export
        // CI, and whether the overlay applies turns on exactly that — a scenario
        // about the overlay would otherwise pass or fail according to where it
        // ran.
        foreach (var name in new[] { "CI", "TEAMCITY_VERSION", "GITHUB_ACTIONS", "BUILD_BUILDID", "JENKINS_URL" })
        {
            startInfo.Environment[name] = string.Empty;
        }

        // And an install root of this scenario's own. Without it the child
        // resolves against the real %LOCALAPPDATA%\Preflight, so a scenario
        // would pass or fail according to which pipelines the person running the
        // suite happens to have installed — the same defect the block above
        // exists to prevent, one variable along.
        startInfo.Environment["LOCALAPPDATA"] = string.Empty;
        startInfo.Environment["PREFLIGHT_HOME"] = InstallRoot.Value.FullName;

        foreach (var (name, value) in _environment)
        {
            startInfo.Environment[name] = value;
        }

        using var process = Process.Start(startInfo);
        process.ShouldNotBeNull();

        // Read before waiting: a child that fills the pipe buffer blocks on the
        // write while the parent blocks on the exit.
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();

        process.WaitForExit(TimeSpan.FromSeconds(60)).ShouldBeTrue("The CLI did not exit within 60s.");

        _standardOutput = output.GetAwaiter().GetResult();
        _standardError = errors.GetAwaiter().GetResult();
        _exitCode = process.ExitCode;
    }
}
