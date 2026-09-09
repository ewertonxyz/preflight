namespace Preflight.Rules.Tests.PreSubmit;

using System.Text;
using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="MergeArtifactRule"/>, the second rule of the set that runs
/// without being configured first.
/// </summary>
public sealed class MergeArtifactRuleTests
{
    private const string Path = "src/Program.cs";

    private readonly MergeArtifactRule _rule = new();

    /// <summary>
    /// What is a marker, and what only looks like one.
    /// </summary>
    /// <remarks>
    /// The row that decides the shape of the rule is the line of equals signs.
    /// It is how a heading is underlined in two of the most common markup
    /// formats there are, so reporting it would fail a correct file — and a
    /// rule that fails something correct is switched off whole rather than
    /// adjusted. The two directional markers and the common-ancestor one have
    /// no other use at the start of a line.
    /// </remarks>
    [Theory]
    [InlineData("<<<<<<< HEAD\n", RuleStatus.Failed)]
    [InlineData(">>>>>>> other\n", RuleStatus.Failed)]
    [InlineData("||||||| base\n", RuleStatus.Failed)]
    [InlineData("=======\n", RuleStatus.Passed)]
    [InlineData("a <<<<<<< HEAD\n", RuleStatus.Passed)]
    [InlineData("<<<<<< HEAD\n", RuleStatus.Passed)]
    [InlineData("a\n<<<<<<< HEAD", RuleStatus.Failed)]
    public async Task ExecuteAsync_ForEachLineShape_ReportsOnlyTheUnambiguousMarkers(
        string content,
        RuleStatus status) =>
        (await Run(content)).Status.ShouldBe(status);

    /// <remarks>
    /// A file conflicted on Windows opens with a byte-order mark, and left in
    /// place it sits in front of the marker on the first line. Without
    /// consuming it the rule goes quiet on the single most common shape of the
    /// thing it exists to catch, and goes quiet without a sound.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAByteOrderMarkBeforeTheMarker_ReportsIt()
    {
        var content = new List<byte> { 0xEF, 0xBB, 0xBF };

        content.AddRange(Encoding.UTF8.GetBytes("<<<<<<< HEAD\n"));

        (await RunBytes([.. content])).Status.ShouldBe(RuleStatus.Failed);
    }

    /// <remarks>
    /// The line that keeps the rule from failing every file with a heading in
    /// it, which is what would get it switched off across a repository.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAHeadingUnderline_Passes()
    {
        var outcome = await Run("Title\n=======\n\nSome prose.\n");

        outcome.Status.ShouldBe(RuleStatus.Passed);
        outcome.Findings.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithAWholeConflict_ReportsOneFindingPerMarkerLine()
    {
        var outcome = await Run(
            "a\n<<<<<<< HEAD\nb\n||||||| base\nc\n=======\nd\n>>>>>>> other\ne\n");

        outcome.Findings.Select(finding => finding.Location!.Line).ShouldBe([2, 4, 8]);
    }

    [Fact]
    public async Task ExecuteAsync_WithAMarker_NamesThePathTheLineAndTheMarker()
    {
        var finding = (await Run("a\n>>>>>>> other\n")).Findings.Single();

        finding.Location!.RelativePath.ShouldBe(Path);
        finding.Location.Line.ShouldBe(2);
        finding.Actual.ShouldBe("a line beginning '>>>>>>>'");
    }

    /// <remarks>
    /// A file with carriage returns is still read line by line, and the marker
    /// is still at the start of one.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_OverAFileWithCarriageReturns_StillFindsTheMarker()
    {
        var outcome = await Run("a\r\n<<<<<<< HEAD\r\nb\r\n");

        outcome.Findings.Single().Location!.Line.ShouldBe(2);
    }

    /// <summary>
    /// The ceiling, and the file that sits exactly on it.
    /// </summary>
    /// <remarks>
    /// A conflict marker lives in a file somebody was editing by hand, which is
    /// not what a very large file is. The boundary rows are here because
    /// off-by-one on a ceiling is the way a limit stops meaning what it says.
    /// </remarks>
    [Theory]
    [InlineData(12, RuleStatus.NotApplicable)]
    [InlineData(13, RuleStatus.Failed)]
    [InlineData(14, RuleStatus.Failed)]
    public async Task ExecuteAsync_AtEachSizeBoundary_ExaminesOrSkips(long maxBytes, RuleStatus status)
    {
        // The file is thirteen bytes, so the ceiling is set one below it, on it,
        // and one above it. A file exactly at the ceiling is examined: a limit
        // that excluded its own value would mean something different from what
        // it is named.
        var outcome = await Run("<<<<<<< HEAD\n", maxBytes);

        outcome.Status.ShouldBe(status);
    }

    /// <summary>
    /// A file the rule declined to open is not a file that passed.
    /// </summary>
    /// <remarks>
    /// The tick over a file nobody read is the small lie that arrives by
    /// counting a file before deciding whether to look at it.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithEveryFileSkipped_ReportsNotApplicableAndNotPassed()
    {
        var outcome = await Run("<<<<<<< HEAD\n", maxBytes: 1);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// A file shorter than a byte-order mark cannot carry one, and the read
    /// that consumes the mark has to answer that without looking past the end
    /// of what it was handed.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAFileShorterThanAByteOrderMark_ReadsItAnyway() =>
        (await Run("a\n")).Status.ShouldBe(RuleStatus.Passed);

    [Fact]
    public async Task ExecuteAsync_WithABinaryFile_ReportsNotApplicable() =>
        (await RunBytes([0x00, (byte)'<', (byte)'<'])).Status.ShouldBe(RuleStatus.NotApplicable);

    [Fact]
    public async Task ExecuteAsync_WithOnlyDeletions_ReportsNotApplicable()
    {
        var outcome = await _rule.ExecuteAsync(
            Context(
                changedFiles: [Deleted(Path)],
                fileSystem: Workspace(Encoding.UTF8.GetBytes("<<<<<<< HEAD\n"))),
            CancellationToken.None);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoChanges_ReportsNotApplicable()
    {
        var outcome = await _rule.ExecuteAsync(Context(), CancellationToken.None);

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
            Context(changedFiles: [Added(Path)], fileSystem: fileSystem),
            CancellationToken.None);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// A stream is entitled to return fewer bytes than it was asked for, and
    /// does over a network share. A single read taken for the whole file would
    /// leave the rule reading half a file and reporting on all of it.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheStreamReturnsShortReads_StillReadsTheWholeFile()
    {
        var content = Encoding.UTF8.GetBytes("a\nb\nc\nd\ne\n<<<<<<< HEAD\n");
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(true);
        fileSystem.GetFileSize(Arg.Any<string>()).Returns(content.Length);
        fileSystem.OpenRead(Arg.Any<string>()).Returns(_ => new ChunkedStream(content, chunk: 3));

        var outcome = await _rule.ExecuteAsync(
            Context(changedFiles: [Added(Path)], fileSystem: fileSystem),
            CancellationToken.None);

        outcome.Findings.Single().Location!.Line.ShouldBe(6);
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
                    fileSystem: Workspace(Encoding.UTF8.GetBytes("<<<<<<< HEAD\n"))),
                source.Token));
    }

    private Task<RuleOutcome> Run(string content, long? maxBytes = null) =>
        RunBytes(Encoding.UTF8.GetBytes(content), maxBytes);

    private Task<RuleOutcome> RunBytes(byte[] content, long? maxBytes = null) =>
        _rule.ExecuteAsync(
            Context(
                changedFiles: [Added(Path)],
                policy: maxBytes is { } bytes ? PolicyWith("maxBytes", bytes) : EmptyPolicy(),
                fileSystem: Workspace(content)),
            CancellationToken.None);

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

    /// <summary>
    /// A stream that never answers with more than a few bytes at a time.
    /// </summary>
    private sealed class ChunkedStream(byte[] content, int chunk) : MemoryStream(content)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, chunk)], cancellationToken);
    }
}
