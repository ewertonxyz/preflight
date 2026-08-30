namespace Preflight.Rules.Tests.PreSubmit;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="ForbiddenPathsRule"/> and the glob language it matches
/// with.
/// </summary>
/// <remarks>
/// The glob table is where this rule is won or lost. A pattern that is
/// accidentally recursive forbids more than the production asked for and gets
/// disabled; one that is accidentally anchored forbids less and lets a secret
/// through. Only the second is silent, which is why the table is exhaustive.
/// </remarks>
public sealed class ForbiddenPathsRuleTests
{
    private static readonly string[] AnyPfx = ["**/*.pfx"];

    private readonly ForbiddenPathsRule _rule = new();

    private Task<RuleOutcome> Run(IReadOnlyList<ChangedFile> changed, string[]? patterns = null) =>
        _rule.ExecuteAsync(
            Context(changed, PolicyWith("patterns", patterns ?? AnyPfx)),
            CancellationToken.None);

    [Fact]
    public async Task ExecuteAsync_WithNoChangedFiles_IsNotApplicable()
    {
        (await Run([])).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// No patterns is not the same as no files: the rule was configured to
    /// forbid nothing, so it examined nothing. <c>Passed</c> would claim a
    /// check that never happened.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithNoPatterns_IsNotApplicable()
    {
        (await Run([Added("src/a.cs")], [])).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithNothingMatching_Passes()
    {
        (await Run([Added("src/a.cs")])).Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public async Task ExecuteAsync_WithAMatch_FailsNamingThePattern()
    {
        var outcome = await Run([Added("secrets/key.pfx")]);

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Actual.ShouldNotBeNull().ShouldContain("**/*.pfx");
    }

    /// <summary>
    /// A secret's path is reported; its content never is.
    /// </summary>
    /// <remarks>
    /// This rule and the compile probe are the two places file content could
    /// enter the report, and the report is not the end of the journey: it goes
    /// into a build log anyone on the team can read, and into the run's stored
    /// history, which is kept. Quoting the line a secret sits on would publish
    /// it to all three at once, permanently.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_ReportsThePathAndNeverTheContent()
    {
        var outcome = await Run([Added("config/.env")], ["**/.env"]);

        var finding = outcome.Findings.ShouldHaveSingleItem();

        finding.Location!.RelativePath.ShouldBe("config/.env");
        finding.Message.ShouldNotContain("=");
    }

    /// <summary>
    /// Deleting a forbidden file is the fix, not the violation.
    /// </summary>
    /// <remarks>
    /// The case a naive implementation gets backwards, and it gets it backwards
    /// in the worst direction: it tells someone their cleanup commit is the
    /// problem, and no commit satisfies the rule.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenAForbiddenFileIsDeleted_DoesNotFail()
    {
        var outcome = await Run([Deleted("secrets/key.pfx"), Added("src/a.cs")]);

        outcome.Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public async Task ExecuteAsync_WithOnlyDeletions_IsNotApplicable()
    {
        (await Run([Deleted("secrets/key.pfx")])).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <summary>
    /// The glob table. Each row is a decision, not a sample.
    /// </summary>
    /// <remarks>
    /// <c>**</c> crosses separators and <c>*</c> does not — collapsing them
    /// makes every pattern accidentally recursive. <c>**/</c> also matches zero
    /// directories, or every pattern would need writing twice. And matching is
    /// case-insensitive, because Windows and macOS filesystems are: a
    /// case-sensitive matcher lets <c>KEY.PFX</c> through on the machines most
    /// developers use.
    /// </remarks>
    [Theory]
    [InlineData("**/*.pfx", "key.pfx", true)]
    [InlineData("**/*.pfx", "deep/nested/key.pfx", true)]
    [InlineData("*.pfx", "key.pfx", true)]
    [InlineData("*.pfx", "deep/key.pfx", false)]
    [InlineData("secrets/*", "secrets/key.txt", true)]
    [InlineData("secrets/*", "secrets/nested/key.txt", false)]
    [InlineData("secrets/**", "secrets/nested/key.txt", true)]
    [InlineData("**/*.pfx", "KEY.PFX", true)]
    [InlineData("**/id_rsa", "home/.ssh/id_rsa", true)]
    [InlineData("**/id_rsa", "home/.ssh/id_rsa.pub", false)]
    [InlineData("src/?.cs", "src/a.cs", true)]
    [InlineData("src/?.cs", "src/ab.cs", false)]
    [InlineData("**/*.local.json", "config/app.local.json", true)]
    [InlineData("**/*.local.json", "config/app.json", false)]
    public async Task ExecuteAsync_AppliesTheGlobSemantics(
        string pattern,
        string path,
        bool shouldMatch)
    {
        var outcome = await Run([Added(path)], [pattern]);

        outcome.Status.ShouldBe(shouldMatch ? RuleStatus.Failed : RuleStatus.Passed);
    }

    /// <remarks>
    /// Two overlapping patterns describe one problem. Reporting it twice makes
    /// the count in the summary line disagree with the number of files a reader
    /// has to fix.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAFileMatchingTwoPatterns_ReportsItOnce()
    {
        var outcome = await Run([Added("secrets/key.pfx")], ["**/*.pfx", "secrets/**"]);

        outcome.Findings.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ExecuteAsync_WithNoPolicy_UsesDefaultPatterns()
    {
        var outcome = await _rule.ExecuteAsync(
            Context([Added("config/.env")], EmptyPolicy()),
            CancellationToken.None);

        outcome.Status.ShouldBe(RuleStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsFindingsInTheOrderTheFilesWereGiven()
    {
        var outcome = await Run(
            [Added("z/last.pfx"), Added("src/a.cs"), Added("a/first.pfx")]);

        outcome.Findings.Select(finding => finding.Location!.RelativePath)
            .ShouldBe(["z/last.pfx", "a/first.pfx"]);
    }

    /// <remarks>
    /// A pre-submit rule receives whatever the change set holds, and a merge or
    /// a generated-asset commit reaches five figures. Ten thousand entries are
    /// generated rather than hand-written, and the one that matters is a
    /// literal: a generated path would make the assertion depend on a seed, so
    /// a regression would surface as an intermittent failure nobody can
    /// reproduce.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_OverTenThousandFiles_FindsThePlantedOne()
    {
        var changed = Enumerable.Range(0, 10_000)
            .Select(index => Added($"src/module-{index}/file.cs"))
            .Append(Added("secrets/planted.pfx"))
            .ToArray();

        var outcome = await Run(changed);

        outcome.Findings.ShouldHaveSingleItem()
            .Location!.RelativePath.ShouldBe("secrets/planted.pfx");
    }

    [Fact]
    public async Task ExecuteAsync_WithACancelledToken_StopsRatherThanFinishing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var changed = Enumerable.Range(0, 5_000)
            .Select(index => Added($"src/file-{index}.cs"))
            .ToArray();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _rule.ExecuteAsync(
                Context(changed, PolicyWith("patterns", AnyPfx)),
                cancellation.Token));
    }
}
