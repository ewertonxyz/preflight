namespace Preflight.Core.Tests.Changes;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Services;
using Preflight.Core;
using Preflight.Core.Changes;
using Preflight.Core.Execution;

/// <summary>
/// Runs the change source against a real git repository.
/// </summary>
/// <remarks>
/// <para>
/// The substituted runner in <see cref="GitChangeSourceTests"/> proves the
/// parser and cannot prove the arguments. A wrong flag, a missing <c>-z</c>, a
/// working directory that never reaches git — every one of those leaves the
/// unit tests green.
/// </para>
/// <para>
/// The repository is built under the system temp directory, never inside the
/// working tree. A <c>.git</c> nested in this repository is not excluded by
/// <c>.gitignore</c>, and the Preflight repository itself is unusable as a
/// fixture because its own diff changes with every commit — a test written
/// against it either asserts nothing stable or gets rewritten until it passes.
/// </para>
/// </remarks>
public sealed class GitChangeSourceIntegrationTests : IDisposable
{
    private readonly DirectoryInfo _repository;
    private readonly ProcessRunner _processes = new();

    public GitChangeSourceIntegrationTests()
    {
        _repository = new DirectoryInfo(
            Path.Combine(Path.GetTempPath(), "preflight-git-" + Guid.NewGuid().ToString("N")));

        _repository.Create();
    }

    public void Dispose()
    {
        try
        {
            // git marks objects read-only, and Directory.Delete refuses those.
            foreach (var file in _repository.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
            }

            _repository.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that survives a run is litter, not a failure.
            // Throwing here would turn a cleanup problem into a red test that
            // says nothing about the code.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task GetChangesAsync_AgainstARealCommit_ReportsTheChange()
    {
        await InitialiseRepository();
        await Write("src/a.cs", "class A;");
        await Git("add", ".");
        await Git("commit", "-m", "second");

        var changes = await Changes("HEAD~1");

        changes.ShouldHaveSingleItem();
        changes[0].ShouldBe(new ChangedFile("src/a.cs", ChangeKind.Added));
    }

    /// <remarks>
    /// The flag that only a real repository can confirm. git detects renames on
    /// its own, and a command missing <c>--name-status</c> or built differently
    /// would report a delete and an add — which the substituted parser would
    /// never see.
    /// </remarks>
    [Fact]
    public async Task GetChangesAsync_AgainstARealRename_CarriesTheOldPath()
    {
        await InitialiseRepository();
        await Write("src/old.cs", new string('x', 200));
        await Git("add", ".");
        await Git("commit", "-m", "add");

        await Git("mv", "src/old.cs", "src/new.cs");
        await Git("commit", "-m", "rename");

        var changes = await Changes("HEAD~1");

        changes.ShouldHaveSingleItem();
        changes[0].Kind.ShouldBe(ChangeKind.Renamed);
        changes[0].RelativePath.ShouldBe("src/new.cs");
        changes[0].PreviousRelativePath.ShouldBe("src/old.cs");
    }

    /// <remarks>
    /// The other half of the <c>-z</c> argument, made of real bytes on a real
    /// filesystem rather than of a string a test author typed.
    /// </remarks>
    [Fact]
    public async Task GetChangesAsync_AgainstARealNonAsciiPath_ReadsItVerbatim()
    {
        await InitialiseRepository();
        await Write("Art/café texture.txt", "content");
        await Git("add", ".");
        await Git("commit", "-m", "asset");

        var changes = await Changes("HEAD~1");

        changes.ShouldHaveSingleItem();
        changes[0].RelativePath.ShouldBe("Art/café texture.txt");
    }

    [Fact]
    public async Task GetChangesAsync_WithNothingChanged_ReturnsAnEmptyList()
    {
        await InitialiseRepository();

        (await Changes("HEAD")).ShouldBeEmpty();
    }

    /// <summary>
    /// A ref that does not resolve is exit 2, and never an empty list.
    /// </summary>
    /// <remarks>
    /// This is the shallow-clone case the non-goals point at. A CI
    /// checkout without <c>origin/main</c> would, on the tempting reading,
    /// produce no changed files, make both pre-submit rules report
    /// <c>NotApplicable</c>, and turn the step green having verified nothing.
    /// The other tempting fix — a <c>git fetch</c> to make the ref resolve — is
    /// a download, which is a declared non-goal.
    /// </remarks>
    [Fact]
    public async Task GetChangesAsync_WithARefThatDoesNotResolve_ThrowsRatherThanReturningNothing()
    {
        await InitialiseRepository();

        await Should.ThrowAsync<ChangeSourceException>(() => Changes("origin/does-not-exist"));
    }

    [Fact]
    public async Task GetChangesAsync_OutsideARepository_Throws()
    {
        await Should.ThrowAsync<ChangeSourceException>(() => Changes("HEAD"));
    }

    /// <remarks>
    /// Detached HEAD is the normal state of a CI checkout, which fetches a SHA
    /// rather than a branch. A change source that assumed a symbolic ref would
    /// work on every developer machine and fail only in the place it matters.
    /// </remarks>
    [Fact]
    public async Task GetChangesAsync_OnADetachedHead_StillResolves()
    {
        await InitialiseRepository();
        await Write("src/a.cs", "class A;");
        await Git("add", ".");
        await Git("commit", "-m", "second");

        var head = await Git("rev-parse", "HEAD");
        await Git("checkout", head.StandardOutput.Trim());

        (await Changes("HEAD~1")).ShouldHaveSingleItem();
    }

    private Task<IReadOnlyList<ChangedFile>> Changes(string fromRef) =>
        new GitChangeSource(_processes).GetChangesAsync(_repository, fromRef, CancellationToken.None);

    private async Task InitialiseRepository()
    {
        await Git("init", "-b", "main");

        // Local, so the machine's own git identity is neither required nor
        // touched. A CI agent usually has none configured, and commit refuses
        // without one.
        await Git("config", "user.email", "tests@preflight.invalid");
        await Git("config", "user.name", "Preflight tests");
        await Git("config", "commit.gpgsign", "false");

        await Write("README.md", "fixture");
        await Git("add", ".");
        await Git("commit", "-m", "first");
    }

    private async Task Write(string relativePath, string content)
    {
        var path = Path.Combine(_repository.FullName, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await File.WriteAllTextAsync(path, content);
    }

    private async Task<ProcessResult> Git(params string[] arguments)
    {
        var result = await _processes.RunAsync(
            new ProcessRequest
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = _repository.FullName,
            },
            CancellationToken.None);

        result.ExitCode.ShouldBe(
            0,
            $"git {string.Join(' ', arguments)} failed: {result.StandardError}");

        return result;
    }
}
