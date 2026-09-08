namespace Preflight.Rules.Tests.PreSubmit;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="CompanionFileRule"/> and the requirement parser it holds.
/// </summary>
/// <remarks>
/// A pair is one string with an arrow in it, because a policy setting carries
/// homogeneous arrays of scalars — an object inside an array would need the
/// parser, the policy node and the scoped reader all to learn a shape none of
/// them has, which is a second contract change to express what an arrow already
/// expresses.
/// </remarks>
public sealed class CompanionFileRuleTests
{
    private static readonly string[] UassetPair = ["**/*.uasset -> {dir}/{name}.uexp"];

    private static readonly string[] NoPairs = [];

    private static readonly string[] PairWithoutAnArrow = ["**/*.uasset"];

    private static readonly string[] PairWithNoPattern = [" -> {dir}/{name}.uexp"];

    private static readonly string[] PairWithNoTemplate = ["**/*.uasset -> "];

    private static readonly string[] TwoPairs =
    [
        "**/*.uasset -> {dir}/{name}.uexp",
        "**/*.uasset -> {dir}/{name}.uinfo",
    ];

    private readonly CompanionFileRule _rule = new();

    private Task<RuleOutcome> Run(
        IReadOnlyList<ChangedFile> changed,
        string[]? pairs = null,
        IFileSystem? fileSystem = null) =>
        _rule.ExecuteAsync(
            Context(
                changedFiles: changed,
                policy: pairs is null ? EmptyPolicy() : PolicyWith("pairs", pairs),
                fileSystem: fileSystem ?? Missing()),
            CancellationToken.None);

    /// <summary>A workspace holding nothing.</summary>
    private static IFileSystem Missing()
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(false);

        return fileSystem;
    }

    /// <summary>A workspace holding exactly these relative paths.</summary>
    private static IFileSystem Holding(params string[] paths)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(call =>
            paths.Any(path => Normalise(call.Arg<string>()).EndsWith(path, StringComparison.Ordinal)));

        return fileSystem;
    }

    private static string Normalise(string path) => path.Replace('\\', '/');

    /// <summary>
    /// The path the rule actually asked the file system about.
    /// </summary>
    private static string Probed(IFileSystem fileSystem) =>
        Normalise((string)fileSystem.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IFileSystem.FileExists))
            .GetArguments()[0]!);

    [Fact]
    public async Task ExecuteAsync_WithNoPairs_IsNotApplicable()
    {
        (await Run([Added("a.uasset")])).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithAnEmptyPairsArray_IsNotApplicable()
    {
        (await Run([Added("a.uasset")], NoPairs)).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// <c>Passed</c> would claim that a pair was checked when nothing matched
    /// the pattern in the first place.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenNoChangedFileMatchesAWhenGlob_IsNotApplicable()
    {
        var outcome = await Run([Added("README.md")], UassetPair);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
        outcome.Status.ShouldNotBe(RuleStatus.Passed);
    }

    /// <remarks>
    /// The companion arriving in the same commit is the common case, and asking
    /// the disk about a file the change set already names would be a syscall per
    /// asset in a commit that can hold thousands.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheCompanionIsInTheChangeSet_PassesWithoutTouchingTheDisk()
    {
        var fileSystem = Missing();

        var outcome = await Run([Added("art/a.uasset"), Added("art/a.uexp")], UassetPair, fileSystem);

        outcome.Status.ShouldBe(RuleStatus.Passed);
        fileSystem.DidNotReceive().FileExists(Arg.Any<string>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheCompanionIsOnlyOnDisk_Passes()
    {
        var outcome = await Run([Added("art/a.uasset")], UassetPair, Holding("art/a.uexp"));

        outcome.Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheCompanionIsNowhere_FailsSayingWhichFileIsMissing()
    {
        var outcome = await Run([Added("art/a.uasset")], UassetPair);

        outcome.Status.ShouldBe(RuleStatus.Failed);

        var finding = outcome.Findings.ShouldHaveSingleItem();

        finding.Expected.ShouldNotBeNull().ShouldContain("art/a.uexp");
        finding.Location.ShouldNotBeNull().RelativePath.ShouldBe("art/a.uasset");
        finding.Remediation.ShouldNotBeNullOrWhiteSpace();
    }

    /// <remarks>
    /// Being named in the change set is not the same as existing. A commit that
    /// adds an asset and deletes its sidecar leaves the pair broken, which is
    /// exactly what this rule is for.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheCompanionIsDeletedInTheSameChangeSet_Fails()
    {
        var outcome = await Run([Added("a.uasset"), Deleted("a.uexp")], UassetPair);

        outcome.Status.ShouldBe(RuleStatus.Failed);
    }

    /// <summary>
    /// The template tokens, including the two that are easy to get wrong.
    /// </summary>
    /// <remarks>
    /// <c>{ext}</c> carries no dot, so a template puts the dot where it wants
    /// one; <c>{dir}</c> is empty at the workspace root and the join emits no
    /// separator there. Both were left open by the first draft of the pair
    /// syntax, and each would have worked on the machine of whoever wrote it and
    /// failed at the top of somebody else's repository.
    /// </remarks>
    [Theory]
    [InlineData("**/*.uasset -> {dir}/{name}.uexp", "art/deep/hero.uasset", "art/deep/hero.uexp")]
    [InlineData("**/*.uasset -> {dir}/{name}.uexp", "hero.uasset", "hero.uexp")]
    [InlineData("**/* -> {dir}/{name}.meta", "art/hero", "art/hero.meta")]
    [InlineData("**/*.c -> {dir}/{name}.{ext}.bak", "a.b.c", "a.b.c.bak")]
    [InlineData("**/*.uasset -> sidecars/fixed.uexp", "art/hero.uasset", "sidecars/fixed.uexp")]
    [InlineData("**/.gitignore -> {dir}/{name}.checked", ".gitignore", ".gitignore.checked")]
    public async Task ExecuteAsync_ExpandsTheTemplateTokens(string pair, string changed, string expected)
    {
        var fileSystem = Missing();

        await _rule.ExecuteAsync(
            Context(
                changedFiles: [Added(changed)],
                policy: PolicyWith("pairs", new[] { pair }),
                fileSystem: fileSystem),
            CancellationToken.None);

        Probed(fileSystem).ShouldEndWith(expected);
    }

    /// <remarks>
    /// A file at the top of the repository has no directory, and a template
    /// written for a nested asset would otherwise produce a leading separator —
    /// a path that resolves somewhere else entirely on a rooted filesystem.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_ForAFileAtTheWorkspaceRoot_ProducesNoLeadingSeparator()
    {
        var fileSystem = Missing();

        await Run([Added("foo.uasset")], UassetPair, fileSystem);

        var probed = Probed(fileSystem);

        probed.ShouldNotContain("//");
        probed.ShouldEndWith("/foo.uexp");
        probed.ShouldNotEndWith("//foo.uexp");
    }

    /// <summary>
    /// A typo in a pair names the policy, never a file.
    /// </summary>
    /// <remarks>
    /// Nothing is wrong with the workspace, so a finding pointing at an asset
    /// would send the reader to fix a file that is perfectly correct.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAMalformedPairEntry_FailsNamingTheEntryRatherThanTheFile()
    {
        var outcome = await Run([Added("a.uasset")], PairWithoutAnArrow);

        outcome.Status.ShouldBe(RuleStatus.Failed);

        var finding = outcome.Findings.ShouldHaveSingleItem();

        finding.Actual.ShouldNotBeNull().ShouldContain("**/*.uasset");
        finding.Message.ShouldContain("pairs");
        finding.Location.ShouldBeNull();
    }

    /// <remarks>
    /// An arrow with nothing on one side of it is as unusable as no arrow at
    /// all, and is the shape a half-finished edit leaves behind.
    /// </remarks>
    [Theory]
    [InlineData("no-pattern")]
    [InlineData("no-template")]
    public async Task ExecuteAsync_WithAnArrowMissingOneSide_FailsNamingTheEntry(string shape)
    {
        var pairs = shape == "no-pattern" ? PairWithNoPattern : PairWithNoTemplate;

        var outcome = await Run([Added("a.uasset")], pairs);

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Message.ShouldContain("pairs");
    }

    /// <remarks>
    /// Two requirements over one file are two things to fix, and the order they
    /// are reported in is the order they were declared — not whichever the
    /// dictionary happened to enumerate first.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAFileMatchingTwoPairs_ReportsBothRequirements()
    {
        var outcome = await Run([Added("art/a.uasset")], TwoPairs);

        outcome.Findings.Count.ShouldBe(2);
        outcome.Findings[0].Expected.ShouldNotBeNull().ShouldContain("a.uexp");
        outcome.Findings[1].Expected.ShouldNotBeNull().ShouldContain("a.uinfo");
    }

    /// <remarks>
    /// Deleting both halves of a pair is the remedy, so requiring the companion
    /// of a deleted file would make the rule impossible to satisfy.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheMatchedFileIsDeleted_DoesNotRequireACompanion()
    {
        (await Run([Deleted("a.uasset")], UassetPair)).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithACancelledToken_StopsRatherThanFinishing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _rule.ExecuteAsync(
                Context(changedFiles: [Added("a.uasset")], policy: PolicyWith("pairs", UassetPair)),
                cancellation.Token));
    }
}
