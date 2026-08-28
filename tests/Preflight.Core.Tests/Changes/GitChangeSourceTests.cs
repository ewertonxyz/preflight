namespace Preflight.Core.Tests.Changes;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Services;
using Preflight.Core;

/// <summary>
/// Fixes the parsing of <c>git diff --name-status -z</c> against a substituted
/// process runner.
/// </summary>
/// <remarks>
/// The substitute proves the parser. It cannot prove the arguments handed to
/// git are the right ones — that is what
/// <see cref="GitChangeSourceIntegrationTests"/> is for, and neither half
/// replaces the other.
/// </remarks>
public sealed class GitChangeSourceTests
{
    private static readonly DirectoryInfo Workspace = new(Path.Combine(Path.GetTempPath(), "preflight-tests"));

    private static IProcessRunner RunnerReturning(string standardOutput, int exitCode = 0, string standardError = "")
    {
        var runner = Substitute.For<IProcessRunner>();

        runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(exitCode, standardOutput, standardError, TimeSpan.FromMilliseconds(12)));

        return runner;
    }

    private static Task<IReadOnlyList<ChangedFile>> Changes(string standardOutput) =>
        new GitChangeSource(RunnerReturning(standardOutput))
            .GetChangesAsync(Workspace, "HEAD~1", CancellationToken.None);

    /// <summary>Builds NUL-separated output the way git does.</summary>
    private static string Fields(params string[] fields) => string.Join('\0', fields) + '\0';

    [Fact]
    public void Name_IsGit()
    {
        new GitChangeSource(RunnerReturning(string.Empty)).Name.ShouldBe("git");
    }

    [Theory]
    [InlineData("A", ChangeKind.Added)]
    [InlineData("M", ChangeKind.Modified)]
    [InlineData("D", ChangeKind.Deleted)]
    public async Task GetChangesAsync_MapsEachSimpleStatus(string status, ChangeKind expected)
    {
        var changes = await Changes(Fields(status, "src/a.cs"));

        changes.ShouldHaveSingleItem();
        changes[0].ShouldBe(new ChangedFile("src/a.cs", expected));
    }

    /// <remarks>
    /// One entry with the old path, never a delete plus an add. The rule context
    /// gives <c>PreviousRelativePath</c> exactly this purpose, and without
    /// rename detection surviving the parse the member is born dead.
    /// </remarks>
    [Fact]
    public async Task GetChangesAsync_ForARename_ProducesOneEntryCarryingTheOldPath()
    {
        var changes = await Changes(Fields("R100", "src/old.cs", "src/new.cs"));

        changes.ShouldHaveSingleItem();
        changes[0].ShouldBe(new ChangedFile("src/new.cs", ChangeKind.Renamed, "src/old.cs"));
    }

    [Fact]
    public async Task GetChangesAsync_ForAPartialRename_StillReadsTheScore()
    {
        var changes = await Changes(Fields("R087", "src/old.cs", "src/new.cs"));

        changes[0].Kind.ShouldBe(ChangeKind.Renamed);
        changes[0].PreviousRelativePath.ShouldBe("src/old.cs");
    }

    /// <remarks>
    /// A copy is a new file to every rule that examines it: a path that did not
    /// exist before and does now. Only a rename carries an old path, because
    /// only a rename means the old one is gone.
    /// </remarks>
    [Fact]
    public async Task GetChangesAsync_ForACopy_ReportsTheNewPathAsAdded()
    {
        var changes = await Changes(Fields("C100", "src/a.cs", "src/b.cs"));

        changes.ShouldHaveSingleItem();
        changes[0].ShouldBe(new ChangedFile("src/b.cs", ChangeKind.Added));
    }

    /// <remarks>
    /// The three statuses <see cref="ChangeKind"/> does not model and that are
    /// nonetheless safe to drop: a type change leaves the content unchanged, an
    /// unmerged path belongs to a conflicted tree a validation run cannot speak
    /// about, and a pairing break needs a flag this command never passes.
    /// </remarks>
    [Theory]
    [InlineData("T")]
    [InlineData("U")]
    [InlineData("B")]
    public async Task GetChangesAsync_ForAStatusWithNoEquivalent_DropsIt(string status)
    {
        (await Changes(Fields(status, "src/a.cs"))).ShouldBeEmpty();
    }

    /// <summary>
    /// Anything unrecognised is an error, not a silent drop.
    /// </summary>
    /// <remarks>
    /// This is the false green of the whole block. A file quietly missing from
    /// the changed set makes <c>core.presubmit.large-file</c> report <c>n/a</c>
    /// on a commit that had something to check — and the built-in rule set already calls
    /// <c>n/a</c> for something unexamined the honest answer, which is exactly
    /// what makes the wrong one invisible.
    /// </remarks>
    [Fact]
    public async Task GetChangesAsync_ForAnUnrecognisedStatus_Throws()
    {
        await Should.ThrowAsync<ChangeSourceException>(() => Changes(Fields("X", "src/a.cs")));
    }

    /// <remarks>
    /// Zero changed files is an answer, not a failure. It is what makes both
    /// pre-submit rules return <c>NotApplicable</c> per the built-in rule set.
    /// </remarks>
    [Fact]
    public async Task GetChangesAsync_WithAnEmptyDiff_ReturnsAnEmptyList()
    {
        (await Changes(string.Empty)).ShouldBeEmpty();
    }

    [Fact]
    public async Task GetChangesAsync_WithSeveralChanges_PreservesGitsOrder()
    {
        var changes = await Changes(Fields("M", "src/b.cs", "A", "src/a.cs", "D", "src/c.cs"));

        changes.Select(change => change.RelativePath).ShouldBe(["src/b.cs", "src/a.cs", "src/c.cs"]);
    }

    /// <remarks>
    /// A changed path reaches <c>FindingLocation.RelativePath</c>, the console
    /// report, the JSON, and every glob a rule matches against. A separator that
    /// varies by operating system breaks a golden file on one and a pattern
    /// match on the other.
    /// </remarks>
    [Fact]
    public async Task GetChangesAsync_NormalisesSeparatorsToForwardSlashes()
    {
        var changes = await Changes(Fields("M", "src\\nested\\a.cs"));

        changes[0].RelativePath.ShouldBe("src/nested/a.cs");
    }

    /// <remarks>
    /// The reason for <c>-z</c>. Without it git escapes this path into octal
    /// and the parser needs a decoder; with it the bytes arrive as they are. In
    /// a game repository with localised assets this is the common case, not the
    /// exotic one.
    /// </remarks>
    [Fact]
    public async Task GetChangesAsync_WithANonAsciiPath_ReadsItVerbatim()
    {
        var changes = await Changes(Fields("M", "Art/café texture.png"));

        changes[0].RelativePath.ShouldBe("Art/café texture.png");
    }

    [Fact]
    public async Task GetChangesAsync_AsksGitForANulSeparatedNameStatusDiff()
    {
        var runner = RunnerReturning(string.Empty);

        await new GitChangeSource(runner).GetChangesAsync(Workspace, "origin/main", CancellationToken.None);

        await runner.Received(1).RunAsync(
            Arg.Is<ProcessRequest>(request =>
                request.FileName == "git" &&
                request.Arguments.Contains("--name-status") &&
                request.Arguments.Contains("-z") &&
                request.Arguments.Contains("origin/main") &&
                request.WorkingDirectory == Workspace.FullName),
            Arg.Any<CancellationToken>());
    }

    /// <remarks>
    /// An argument list, never a concatenated command line, so a ref carrying a
    /// space or a quote cannot become a second argument. The command surface takes
    /// <c>--changed-from</c> straight from the user.
    /// </remarks>
    [Fact]
    public async Task GetChangesAsync_PassesTheRefAsASingleArgument()
    {
        var runner = RunnerReturning(string.Empty);

        await new GitChangeSource(runner).GetChangesAsync(
            Workspace, "a ref with spaces", CancellationToken.None);

        await runner.Received(1).RunAsync(
            Arg.Is<ProcessRequest>(request => request.Arguments.Contains("a ref with spaces")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetChangesAsync_WhenGitFails_ThrowsAConfigurationErrorNamingTheStandardError()
    {
        var source = new GitChangeSource(
            RunnerReturning(string.Empty, 128, "fatal: ambiguous argument 'nope'"));

        var exception = await Should.ThrowAsync<ChangeSourceException>(() =>
            source.GetChangesAsync(Workspace, "nope", CancellationToken.None));

        exception.Message.ShouldContain("fatal: ambiguous argument 'nope'");
        exception.ShouldBeAssignableTo<ConfigurationLoadException>();
    }

    /// <remarks>
    /// The CLI refuses this first, but the engine is hostable
    /// without the CLI. Returning an empty list here would make every
    /// pre-submit rule report <c>NotApplicable</c> and the run go green having
    /// examined nothing.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetChangesAsync_WithoutARef_Throws(string? fromRef)
    {
        var source = new GitChangeSource(RunnerReturning(string.Empty));

        await Should.ThrowAsync<ChangeSourceException>(() =>
            source.GetChangesAsync(Workspace, fromRef, CancellationToken.None));
    }

    [Fact]
    public async Task GetChangesAsync_PassesTheCancellationTokenThrough()
    {
        var runner = RunnerReturning(string.Empty);
        using var cancellation = new CancellationTokenSource();

        await new GitChangeSource(runner).GetChangesAsync(Workspace, "HEAD", cancellation.Token);

        await runner.Received(1).RunAsync(Arg.Any<ProcessRequest>(), cancellation.Token);
    }

    /// <remarks>
    /// git never emits a truncated record, so these guard the parser against
    /// producing a half-built entry rather than against git. A record with a
    /// status and no path, or a rename with only one of its two paths, stops the
    /// walk instead of indexing past the end.
    /// </remarks>
    [Theory]
    [InlineData("M")]
    [InlineData("R100")]
    [InlineData("R100\0src/old.cs")]
    public async Task GetChangesAsync_WithATruncatedRecord_StopsWithoutThrowing(string output)
    {
        (await Changes(output)).ShouldBeEmpty();
    }
}
