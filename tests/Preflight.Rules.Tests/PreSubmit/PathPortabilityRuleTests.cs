namespace Preflight.Rules.Tests.PreSubmit;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="PathPortabilityRule"/> and the four checks it composes.
/// </summary>
/// <remarks>
/// <para>
/// The checks are internal and are driven through the rule, which is also the
/// only place their behaviour is observable.
/// </para>
/// <para>
/// Almost none of this could be a fixture on disk. <c>CON</c>, a colon in a
/// name and a pair of files differing only in case cannot be created in a
/// Windows working tree at all, and the three pipelines that run this suite are
/// on Windows. The rule reads the change set rather than the tree precisely so
/// that these paths can be described without existing.
/// </para>
/// </remarks>
public sealed class PathPortabilityRuleTests
{
    private readonly PathPortabilityRule _rule = new();

    /// <summary>
    /// One-check selections, as fields because an array written at the call
    /// site is rebuilt on every invocation and the analysers say so.
    /// </summary>
    private static readonly string[] NoChecks = [];

    private static readonly string[] InvalidCharacterOnly = ["invalid-character"];

    private static readonly string[] ReservedNameOnly = ["reserved-name"];

    private static readonly string[] PathLengthOnly = ["path-length"];

    private static readonly string[] UnknownCheck = ["reservedNmae"];

    private static readonly string[] ChecksThatNeverReadTheDisk =
        ["invalid-character", "reserved-name", "path-length"];

    /// <summary>
    /// A path past the whole-path limit whose components are all short.
    /// </summary>
    /// <remarks>
    /// Built from many small components on purpose. One enormous name would be
    /// over both bounds at once and produce two findings, which would make a
    /// test counting one finding per check measure something else entirely.
    /// </remarks>
    private static readonly string TooLongPath = string.Join('/', Enumerable.Repeat("dir", 70));

    /// <summary>A policy selecting exactly these checks.</summary>
    private static Dictionary<string, object?> Selecting(string[] names) =>
        new() { ["checks"] = names };

    private Task<RuleOutcome> Run(
        IReadOnlyList<ChangedFile> changed,
        IFileSystem? fileSystem = null,
        IReadOnlyDictionary<string, object?>? policy = null) =>
        _rule.ExecuteAsync(
            Context(
                changedFiles: changed,
                policy: policy is null ? EmptyPolicy() : PolicyWith(policy),
                fileSystem: fileSystem ?? EmptyDisk()),
            CancellationToken.None);

    /// <summary>
    /// A workspace in which no parent directory exists yet.
    /// </summary>
    /// <remarks>
    /// The state of a change set that only adds files, and the one that must
    /// not make the rule reach for a listing that would throw.
    /// </remarks>
    private static IFileSystem EmptyDisk()
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.DirectoryExists(Arg.Any<string>()).Returns(false);

        return fileSystem;
    }

    /// <summary>
    /// A workspace whose directories hold the given files.
    /// </summary>
    private static IFileSystem DiskWith(IReadOnlyDictionary<string, string[]> directories)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.DirectoryExists(Arg.Any<string>()).Returns(call =>
            directories.Keys.Any(directory => Tail(call.Arg<string>(), directory)));

        fileSystem.EnumerateFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>()).Returns(call =>
        {
            var match = directories.FirstOrDefault(entry => Tail(call.ArgAt<string>(0), entry.Key));

            return match.Value is null
                ? []
                : match.Value.Select(name => Path.Combine(WorkspaceRoot.FullName, name.Replace('/', Path.DirectorySeparatorChar)));
        });

        return fileSystem;
    }

    private static bool Tail(string path, string relative) =>
        path.Replace('\\', '/').TrimEnd('/').EndsWith(relative.TrimEnd('/'), StringComparison.Ordinal);

    private static string Messages(RuleOutcome outcome) =>
        string.Join(" | ", outcome.Findings.Select(finding => $"{finding.Message} {finding.Actual}"));

    [Fact]
    public async Task ExecuteAsync_WithNoChangedFiles_IsNotApplicable()
    {
        (await Run([])).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// Deleting a path that cannot be checked out is the fix, not another
    /// violation.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithOnlyDeletions_IsNotApplicable()
    {
        (await Run([Deleted("CON.txt")])).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithAnEmptyChecksArray_IsNotApplicable()
    {
        var outcome = await Run(
            [Added("CON.txt")],
            policy: Selecting(NoChecks));

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoChecksConfigured_RunsAllFour()
    {
        var disk = DiskWith(new Dictionary<string, string[]> { ["a"] = ["a/Foo.txt"] });

        var outcome = await Run(
            [Added("a/foo.txt"), Added("bad:name.txt"), Added("CON.txt"), Added(TooLongPath)],
            disk);

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.Count.ShouldBe(4, Messages(outcome));
    }

    /// <summary>
    /// The policy selects checks by name, and only those run.
    /// </summary>
    /// <remarks>
    /// A check turned off has to leave no trace in the report. Asserting the
    /// count alone would pass a rule that ran all four and filtered the
    /// findings afterwards, which costs the listing the case-collision check
    /// performs whether or not anybody asked for it.
    /// </remarks>
    [Theory]
    [InlineData("case-collision", 1)]
    [InlineData("invalid-character", 1)]
    [InlineData("reserved-name", 1)]
    [InlineData("path-length", 1)]
    [InlineData("reserved-name,path-length", 2)]
    public async Task ExecuteAsync_WithOneCheckSelected_RunsOnlyThatOne(string checks, int expected)
    {
        var disk = DiskWith(new Dictionary<string, string[]> { ["a"] = ["a/Foo.txt"] });

        var outcome = await Run(
            [Added("a/foo.txt"), Added("bad:name.txt"), Added("CON.txt"), Added(TooLongPath)],
            disk,
            Selecting(checks.Split(',')));

        outcome.Findings.Count.ShouldBe(expected, Messages(outcome));
    }

    /// <summary>
    /// A typo in the check list fails loudly rather than turning a check off.
    /// </summary>
    /// <remarks>
    /// Settings are opaque to the loader by contract, so nothing upstream can
    /// catch a misspelled name. Ignoring it silently would leave a check
    /// switched off and a report green, which is the worst outcome this tool can
    /// produce.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAnUnknownCheckName_FailsNamingTheNamesThatExist()
    {
        var outcome = await Run(
            [Added("src/a.cs")],
            policy: Selecting(UnknownCheck));

        outcome.Status.ShouldBe(RuleStatus.Failed);

        var finding = outcome.Findings.ShouldHaveSingleItem();

        finding.Actual.ShouldNotBeNull().ShouldContain("reservedNmae");

        foreach (var name in PathPortabilityRule.DefaultChecks)
        {
            finding.Expected.ShouldNotBeNull().ShouldContain(name);
        }

        // The policy key and the entry, never a file: nothing is wrong with the
        // workspace here.
        finding.Location.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithTwoChangedPathsDifferingOnlyInCase_FailsNamingBoth()
    {
        var outcome = await Run([Added("a/Foo.txt"), Added("a/foo.txt")]);

        outcome.Status.ShouldBe(RuleStatus.Failed);

        var finding = outcome.Findings.ShouldHaveSingleItem();

        finding.Actual.ShouldNotBeNull().ShouldContain("a/Foo.txt");
        finding.Actual.ShouldNotBeNull().ShouldContain("a/foo.txt");
    }

    /// <summary>
    /// The case that actually happens: a new file colliding with one that has
    /// been in the repository for years.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenAChangedPathCollidesWithASiblingOnDisk_Fails()
    {
        var disk = DiskWith(new Dictionary<string, string[]> { ["a"] = ["a/Foo.txt"] });

        var outcome = await Run([Added("a/foo.txt")], disk);

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Actual.ShouldNotBeNull().ShouldContain("a/Foo.txt");
    }

    /// <summary>
    /// A modified file is not a collision with itself.
    /// </summary>
    /// <remarks>
    /// The false positive that would fail every clean commit, because a changed
    /// file is nearly always already sitting in its own directory.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheOnlySiblingIsTheFileItself_DoesNotReportACollision()
    {
        var disk = DiskWith(new Dictionary<string, string[]> { ["a"] = ["a/foo.txt"] });

        (await Run([Modified("a/foo.txt")], disk)).Status.ShouldBe(RuleStatus.Passed);
    }

    /// <remarks>
    /// A file added into a directory that does not exist yet is the ordinary
    /// case, and listing it would throw out of the rule and be reported as the
    /// tool erroring on a perfectly good commit.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheParentDirectoryDoesNotExist_DoesNotError()
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.DirectoryExists(Arg.Any<string>()).Returns(false);
        fileSystem.EnumerateFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
            .Returns<IEnumerable<string>>(_ => throw new DirectoryNotFoundException());

        var outcome = await Run([Added("new/dir/a.txt")], fileSystem);

        outcome.Status.ShouldNotBe(RuleStatus.Errored);
        fileSystem.DidNotReceive().EnumerateFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>());
    }

    /// <summary>
    /// The cost is bounded by the change set, never by the repository.
    /// </summary>
    /// <remarks>
    /// One listing per distinct parent directory, deduplicated before the loop,
    /// and never recursive. A pre-submit check that walked the tree would be the
    /// exact cost this stage exists in order not to pay.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_ListsEachParentDirectoryOnceAndNeverRecursively()
    {
        var disk = DiskWith(new Dictionary<string, string[]> { ["a"] = [], ["b"] = [] });

        await Run([Added("a/one.txt"), Added("a/two.txt"), Added("a/three.txt"), Added("b/four.txt")], disk);

        foreach (var directory in new[] { "a", "b" })
        {
            disk.Received(1).EnumerateFiles(
                Arg.Is<string>(path => Tail(path, directory)),
                Arg.Any<string>(),
                SearchOption.TopDirectoryOnly);
        }

        disk.DidNotReceive().EnumerateFiles(
            Arg.Any<string>(),
            Arg.Any<string>(),
            SearchOption.AllDirectories);
    }

    /// <summary>
    /// Files only, and the finding says so.
    /// </summary>
    /// <remarks>
    /// There is no way to list directories through the contract a rule is
    /// handed, and adding one would be a member on the interface every plugin
    /// implements. The wording is what keeps a reader from concluding the rule
    /// is broken when a directory pair goes unreported.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_ComparesAgainstSiblingFilesOnly_NotSiblingDirectories()
    {
        var quiet = DiskWith(new Dictionary<string, string[]> { ["a"] = [] });

        (await Run([Added("a/Sub.txt")], quiet)).Status.ShouldBe(RuleStatus.Passed);

        var colliding = DiskWith(new Dictionary<string, string[]> { ["a"] = ["a/SUB.txt"] });
        var outcome = await Run([Added("a/Sub.txt")], colliding);

        outcome.Findings.ShouldHaveSingleItem().Message.ShouldContain("file");
    }

    /// <remarks>
    /// Renaming a file to fix its case is the remedy, so reporting it would
    /// make the rule impossible to satisfy.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_ForARenameChangingOnlyCase_DoesNotCollideWithItself()
    {
        (await Run([Renamed("a/Foo.cs", "a/foo.cs")])).Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public async Task ExecuteAsync_WithTheSameStemInDifferentDirectories_DoesNotCollide()
    {
        (await Run([Added("a/foo.txt"), Added("b/Foo.txt")])).Status.ShouldBe(RuleStatus.Passed);
    }

    /// <summary>
    /// The characters Windows refuses, and the ones it does not.
    /// </summary>
    /// <remarks>
    /// The forward slash is absent from the refused set because it is this
    /// tool's separator and arrives as structure rather than as content — a
    /// check that refused it would refuse every path in a subdirectory.
    /// </remarks>
    [Theory]
    [InlineData("a<b.txt", true)]
    [InlineData("a>b.txt", true)]
    [InlineData("a:b.txt", true)]
    [InlineData("a\"b.txt", true)]
    [InlineData("a|b.txt", true)]
    [InlineData("a?b.txt", true)]
    [InlineData("a*b.txt", true)]
    [InlineData("a\u0001b.txt", true)]
    [InlineData("a\u001fb.txt", true)]
    [InlineData("a/b.txt", false)]
    [InlineData("a.txt", false)]
    public async Task ExecuteAsync_RefusesTheCharactersWindowsDoes(string path, bool refused)
    {
        var outcome = await Run(
            [Added(path)],
            policy: Selecting(InvalidCharacterOnly));

        outcome.Status.ShouldBe(refused ? RuleStatus.Failed : RuleStatus.Passed, Messages(outcome));
    }

    /// <summary>
    /// All twenty-two device names, in the three positions each can occupy.
    /// </summary>
    /// <remarks>
    /// Generated from the list rather than written as sixty-six rows, so the
    /// twenty-two names appear exactly once and a name added to the check
    /// cannot be added to the test by copying the row above it.
    /// </remarks>
    public static TheoryData<string> ReservedPaths()
    {
        string[] reserved =
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        ];

        var data = new TheoryData<string>();

        foreach (var name in reserved)
        {
            data.Add(name);
            data.Add($"{name}.txt");
            data.Add($"src/{name}/file.cs");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ReservedPaths))]
    public async Task ExecuteAsync_RefusesEveryReservedDeviceName(string path)
    {
        var outcome = await Run(
            [Added(path)],
            policy: Selecting(ReservedNameOnly));

        outcome.Status.ShouldBe(RuleStatus.Failed, path);
        outcome.Findings.ShouldHaveSingleItem();
    }

    /// <remarks>
    /// The over-matching that a prefix test or a regular expression written in a
    /// hurry produces. <c>COM0</c> and <c>COM10</c> are ordinary names, and a
    /// rule that refused them would be unfixable except by disabling it.
    /// </remarks>
    [Theory]
    [InlineData("console.txt")]
    [InlineData("com0.txt")]
    [InlineData("com10.txt")]
    [InlineData("nul2")]
    [InlineData("lpt10.txt")]
    public async Task ExecuteAsync_DoesNotRefuseANameThatMerelyStartsLikeOne(string path)
    {
        var outcome = await Run(
            [Added(path)],
            policy: Selecting(ReservedNameOnly));

        outcome.Status.ShouldBe(RuleStatus.Passed, Messages(outcome));
    }

    /// <summary>
    /// Both limits, at the boundary rather than near it.
    /// </summary>
    [Theory]
    [InlineData(259, 10, true)]
    [InlineData(260, 10, true)]
    [InlineData(261, 10, false)]
    [InlineData(100, 254, true)]
    [InlineData(100, 255, true)]
    [InlineData(100, 256, false)]
    public async Task ExecuteAsync_AppliesTheLengthLimitsAtTheirBoundaries(
        int pathLength,
        int componentLength,
        bool accepted)
    {
        var outcome = await Run(
            [Added(PathOf(pathLength, componentLength))],
            policy: Selecting(PathLengthOnly));

        outcome.Status.ShouldBe(accepted ? RuleStatus.Passed : RuleStatus.Failed, Messages(outcome));
    }

    /// <summary>
    /// A path of exactly <paramref name="pathLength"/> characters whose last
    /// component is exactly <paramref name="componentLength"/> long.
    /// </summary>
    private static string PathOf(int pathLength, int componentLength)
    {
        var last = new string('b', componentLength);

        return pathLength <= componentLength
            ? last
            : new string('a', pathLength - componentLength - 1) + "/" + last;
    }

    /// <summary>
    /// The verdict does not depend on where the repository was checked out.
    /// </summary>
    /// <remarks>
    /// The absolute path is the number that actually breaks on disk, and it is
    /// also a number that differs between two machines validating the same
    /// commit. Measuring it would give one repository two verdicts, which is the
    /// one thing this tool must never do.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_MeasuresThePathRelativeToTheWorkspaceRoot()
    {
        var path = PathOf(261, 10);
        var policy = PolicyWith(Selecting(PathLengthOnly));

        var shallow = await _rule.ExecuteAsync(
            Context(changedFiles: [Added(path)], policy: policy, workspaceRoot: new DirectoryInfo(@"C:\w")),
            CancellationToken.None);

        var deep = await _rule.ExecuteAsync(
            Context(
                changedFiles: [Added(path)],
                policy: policy,
                workspaceRoot: new DirectoryInfo(@"C:\build\agents\7\workspace\very\deep\checkout")),
            CancellationToken.None);

        deep.Status.ShouldBe(shallow.Status);
        deep.Findings.Single().Actual.ShouldBe(shallow.Findings.Single().Actual);
    }

    /// <remarks>
    /// A path over both limits is two problems with two remedies — move it, and
    /// rename the component — so it is reported twice, in the order the checks
    /// were composed.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAPathOverBothLimits_ReportsOncePerCheck()
    {
        var outcome = await Run(
            [Added(PathOf(300, 280))],
            policy: Selecting(PathLengthOnly));

        outcome.Findings.Count.ShouldBe(2, Messages(outcome));
        outcome.Findings[0].Expected.ShouldNotBeNull().ShouldContain("260");
        outcome.Findings[1].Expected.ShouldNotBeNull().ShouldContain("255");
    }

    /// <summary>
    /// Three of the four checks never touch the disk.
    /// </summary>
    /// <remarks>
    /// This is what makes them testable at all on the Windows machines that run
    /// this suite: <c>CON</c>, a colon in a name and a control character cannot
    /// exist in a working tree there. A check that confirmed a path against the
    /// filesystem would be a check nobody could arrange a failing case for.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_ForTheCharacterLengthAndReservedChecks_NeverTouchesTheFileSystem()
    {
        var fileSystem = Substitute.For<IFileSystem>();

        var outcome = await Run(
            [Added("bad:name.txt"), Added("CON.txt"), Added(TooLongPath)],
            fileSystem,
            Selecting(ChecksThatNeverReadTheDisk));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        fileSystem.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithACancelledToken_StopsRatherThanFinishing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _rule.ExecuteAsync(
                Context(changedFiles: [Added("a/foo.txt")], fileSystem: EmptyDisk()),
                cancellation.Token));
    }
}
