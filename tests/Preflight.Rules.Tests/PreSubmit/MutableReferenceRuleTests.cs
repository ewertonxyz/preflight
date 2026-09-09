namespace Preflight.Rules.Tests.PreSubmit;

using System.Text;
using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="MutableReferenceRule"/>, and with it the policy entry it
/// parses and compiles.
/// </summary>
/// <remarks>
/// The entry has no test file of its own. It is internal, this repository has
/// no <c>InternalsVisibleTo</c> by decision, and a collaborator is never
/// surface — so every branch of it is driven through the rule that holds it.
/// </remarks>
public sealed class MutableReferenceRuleTests
{
    private const string Path = "ci/pipeline.yml";

    private static readonly string Sha = new('a', 40);

    private static readonly string[] ActionPin = [@"**/*.yml -> uses: \S+@(\S+)"];

    private readonly MutableReferenceRule _rule = new();

    /// <remarks>
    /// No default entries, because which third-party formats a production
    /// refers to is a fact about its pipeline. A plausible-looking default here
    /// would be somebody's policy written in C#.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithNoEntries_ReportsNotApplicable()
    {
        var outcome = await Run("uses: some/action@main\n", entries: []);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <summary>
    /// A malformed entry is reported against the policy, before a file is
    /// opened.
    /// </summary>
    /// <remarks>
    /// One report per typo rather than one per file that happened to match
    /// nothing — and a failure rather than an errored rule, because the mistake
    /// is in what somebody wrote and not in the tool that read it.
    ///
    /// The last two rows are the price of matching without backtracking,
    /// written down rather than discovered: it is linear by construction, which
    /// removes any possibility of an expression from policy running away on
    /// some file nobody anticipated, and in exchange it refuses backreferences
    /// and lookaround. An entry using either lands here, which is a far better
    /// message than an expired deadline.
    /// </remarks>
    [Theory]
    [InlineData("no arrow at all")]
    [InlineData("**/*.yml -> ")]
    [InlineData(" -> (\\S+)")]
    [InlineData("**/*.yml -> uses: (")]
    [InlineData("**/*.yml -> uses: \\S+")]
    [InlineData("**/*.yml -> uses: (\\S+)@(\\S+)")]
    [InlineData("**/*.yml -> (?<=x)(\\S+)")]
    [InlineData("**/*.yml -> (\\S+)\\1")]
    public async Task ExecuteAsync_ForEachMalformedEntry_ReportsItBeforeOpeningAnyFile(string entry)
    {
        var fileSystem = Workspace("uses: some/action@main\n");

        var outcome = await _rule.ExecuteAsync(
            Context(
                changedFiles: [Added(Path)],
                policy: PolicyWith(Settings([entry], null, null)),
                fileSystem: fileSystem),
            CancellationToken.None);

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.Single().Message.ShouldContain("'pinned'");

        fileSystem.DidNotReceive().OpenRead(Arg.Any<string>());
    }

    /// <remarks>
    /// Counted through the compiled expression rather than by looking for
    /// brackets in the text, which gets it wrong in both directions and gets it
    /// wrong silently: a non-capturing group looks like a capture, and an
    /// escaped bracket does not look like anything.
    /// </remarks>
    [Theory]
    [InlineData(@"uses: \S+@(\S+)", true)]
    [InlineData(@"uses: \S+@(?<sha>\S+)", true)]
    [InlineData(@"(?:uses): \S+@(\S+)", true)]
    [InlineData(@"uses: \S+@\(\S+\)", false)]
    public async Task ExecuteAsync_CountsCapturesTheWayAPolicyAuthorExpects(string expression, bool valid)
    {
        var outcome = await Run($"uses: some/action@{Sha}\n", [$"**/*.yml -> {expression}"]);

        outcome.Status.ShouldBe(valid ? RuleStatus.Passed : RuleStatus.Failed);
    }

    /// <summary>
    /// What counts as immutable, and what does not.
    /// </summary>
    /// <remarks>
    /// The upper-case row is why the default shape is case-sensitive: the
    /// client prints lower case, so accepting anything else is accepting
    /// whatever somebody happened to paste. The row with a trailing newline is
    /// why the shape is anchored at the very end of the input rather than at
    /// the end of a line — a capture running on into the next line would
    /// otherwise satisfy the check whose whole purpose is to refuse a reference
    /// that is not exactly an object name.
    /// </remarks>
    [Theory]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null, RuleStatus.Passed)]
    [InlineData("latest", null, RuleStatus.Failed)]
    [InlineData("main", null, RuleStatus.Failed)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null, RuleStatus.Failed)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null, RuleStatus.Failed)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", null, RuleStatus.Failed)]
    [InlineData("v3", @"\Av\d+\z", RuleStatus.Passed)]
    [InlineData("v", @"\Av\d+\z", RuleStatus.Failed)]
    public async Task ExecuteAsync_ForEachCaptureAndShape_JudgesAsExpected(
        string reference,
        string? immutable,
        RuleStatus status)
    {
        var outcome = await Run($"uses: some/action@{reference}\n", ActionPin, immutable);

        outcome.Status.ShouldBe(status);
    }

    /// <remarks>
    /// The end anchor of the default shape. A capture that swallowed the line
    /// terminator would pass a check written to refuse exactly that.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithACaptureRunningPastTheEndOfTheLine_ReportsIt()
    {
        var outcome = await Run(
            $"uses: some/action@{Sha}\n",
            [@"**/*.yml -> uses: \S+@([\s\S]+)"]);

        outcome.Status.ShouldBe(RuleStatus.Failed);
    }

    /// <remarks>
    /// A shape that will not compile is the policy's mistake, exactly as a
    /// malformed entry is, and it is reported the same way rather than as the
    /// tool breaking.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAShapeThatWillNotCompile_ReportsItAgainstThePolicy()
    {
        var outcome = await Run($"uses: some/action@{Sha}\n", ActionPin, immutable: "(");

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.Single().Message.ShouldContain("'immutable'");
    }

    [Fact]
    public async Task ExecuteAsync_WithAMutableReference_ReportsThePathTheLineAndTheCapture()
    {
        var outcome = await Run("a\nb\nc\nuses: some/action@main\n", ActionPin);

        var finding = outcome.Findings.Single();

        finding.Location!.RelativePath.ShouldBe(Path);
        finding.Location.Line.ShouldBe(4);
        finding.Actual.ShouldBe("main");
    }

    /// <remarks>
    /// One-based, because a report naming line zero sends the reader to a line
    /// no editor has.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAMatchOnTheFirstLine_ReportsLineOne()
    {
        var outcome = await Run("uses: some/action@main\n", ActionPin);

        outcome.Findings.Single().Location!.Line.ShouldBe(1);
    }

    /// <remarks>
    /// Line feeds only, so that a file with carriage returns is numbered the
    /// way every editor numbers it.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_OverAFileWithCarriageReturns_CountsLinesByNewlineAlone()
    {
        var outcome = await Run("a\r\nb\r\nuses: some/action@main\r\n", ActionPin);

        outcome.Findings.Single().Location!.Line.ShouldBe(3);
    }

    /// <remarks>
    /// In the order the policy declared them, so that a file matching two
    /// entries is reported the way the reader wrote them down.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAFileMatchingTwoEntries_ReportsBothInDeclarationOrder()
    {
        var outcome = await Run(
            "uses: some/action@main\nimage: some/image:latest\n",
            [@"**/*.yml -> uses: \S+@(\S+)", @"**/*.yml -> image: \S+:(\S+)"]);

        outcome.Findings.Select(finding => finding.Actual).ShouldBe(["main", "latest"]);
    }

    /// <remarks>
    /// Split at the first arrow. A path pattern never legitimately contains
    /// one and an expression frequently does, so splitting at the last would
    /// break the entry that looks for an arrow in the text it reads.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAnExpressionContainingAnArrow_SplitsAtTheFirst()
    {
        var outcome = await Run("uses: a->b@main\n", [@"**/*.yml -> uses: \S+->\S+@(\S+)"]);

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.Single().Actual.ShouldBe("main");
    }

    /// <summary>
    /// A file the rule declined to open is not a file that passed.
    /// </summary>
    /// <remarks>
    /// A path pattern from policy can match anything a commit touched, so the
    /// two ceilings are what keep an over-broad entry from turning a package
    /// into an out-of-memory failure the tool would report as its own defect.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAnOversizedFile_ReportsNotApplicable()
    {
        var outcome = await Run("uses: some/action@main\n", ActionPin, maxBytes: 4);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithABinaryFile_ReportsNotApplicable()
    {
        var outcome = await RunBytes([0x00, 0x01, 0x02], ActionPin);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoFileMatchingAnyPattern_ReportsNotApplicable()
    {
        var outcome = await Run(
            "uses: some/action@main\n",
            [@"**/*.json -> uses: \S+@(\S+)"]);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithOnlyDeletions_ReportsNotApplicable()
    {
        var outcome = await _rule.ExecuteAsync(
            Context(
                changedFiles: [Deleted(Path)],
                policy: PolicyWith(Settings(ActionPin, null, null)),
                fileSystem: Workspace("uses: some/action@main\n")),
            CancellationToken.None);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// The change set describes a diff, not the disk, so a file named there may
    /// not be here to open — and opening it would error an ordinary commit.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAChangedFileThatIsNotOnDisk_ReportsNotApplicable()
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(false);

        var outcome = await _rule.ExecuteAsync(
            Context(
                changedFiles: [Added(Path)],
                policy: PolicyWith(Settings(ActionPin, null, null)),
                fileSystem: fileSystem),
            CancellationToken.None);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// A file written on Windows opens with a byte-order mark, and left in
    /// place it shifts every offset by one and defeats an expression anchored at
    /// the start of the file.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAByteOrderMark_ReadsTheFirstLineAsTheFirstLine()
    {
        var content = new List<byte> { 0xEF, 0xBB, 0xBF };

        content.AddRange(Encoding.UTF8.GetBytes("uses: some/action@main\n"));

        var outcome = await RunBytes([.. content], [@"**/*.yml -> \Auses: \S+@(\S+)"]);

        outcome.Status.ShouldBe(RuleStatus.Failed);
    }

    /// <remarks>
    /// An expression written a little too broadly can capture a great deal more
    /// than a version, and every field of a finding reaches a build log.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAVeryLongCapture_TruncatesIt()
    {
        var outcome = await Run(
            "uses: some/action@" + new string('z', 500) + "\n",
            ActionPin);

        outcome.Findings.Single().Actual!.Length.ShouldBeLessThan(200);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_DoesNotSwallowTheCancellation()
    {
        using var source = new CancellationTokenSource();

        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _rule.ExecuteAsync(
                Context(
                    changedFiles: [Added(Path)],
                    policy: PolicyWith(Settings(ActionPin, null, null)),
                    fileSystem: Workspace("uses: some/action@main\n")),
                source.Token));
    }

    private Task<RuleOutcome> Run(
        string content,
        IReadOnlyList<string> entries,
        string? immutable = null,
        long? maxBytes = null) =>
        RunBytes(Encoding.UTF8.GetBytes(content), entries, immutable, maxBytes);

    private Task<RuleOutcome> RunBytes(
        byte[] content,
        IReadOnlyList<string> entries,
        string? immutable = null,
        long? maxBytes = null) =>
        _rule.ExecuteAsync(
            Context(
                changedFiles: [Added(Path)],
                policy: PolicyWith(Settings(entries, immutable, maxBytes)),
                fileSystem: Workspace(content)),
            CancellationToken.None);

    private static Dictionary<string, object?> Settings(
        IReadOnlyList<string> entries,
        string? immutable,
        long? maxBytes)
    {
        var settings = new Dictionary<string, object?> { ["pinned"] = entries.ToArray() };

        if (immutable is not null)
        {
            settings["immutable"] = immutable;
        }

        if (maxBytes is { } bytes)
        {
            settings["maxBytes"] = bytes;
        }

        return settings;
    }

    private static IFileSystem Workspace(string content) => Workspace(Encoding.UTF8.GetBytes(content));

    /// <remarks>
    /// The path is matched by its tail, because the rule joins it with a
    /// workspace root the fixture owns and a comparison that rebuilt the join
    /// would be asserting the arrangement rather than the rule.
    /// </remarks>
    private static IFileSystem Workspace(byte[] content)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(call => Tail(call.Arg<string>()));
        fileSystem.GetFileSize(Arg.Any<string>()).Returns(content.Length);
        fileSystem.OpenRead(Arg.Is<string>(path => Tail(path))).Returns(_ => new MemoryStream(content));

        return fileSystem;
    }

    private static bool Tail(string path) =>
        path.Replace('\\', '/').EndsWith(Path, StringComparison.Ordinal);
}
