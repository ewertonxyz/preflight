namespace Preflight.Rules.Tests.PreSubmit;

using System.Text;
using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="LineEndingsRule"/>, and with it the line-ending axis of the
/// attributes reader and the shared head probe.
/// </summary>
/// <remarks>
/// Neither collaborator has a test file of its own. They are internal, this
/// repository has no <c>InternalsVisibleTo</c> by decision, and a collaborator
/// is never surface — so every branch of both is driven through the rule that
/// holds them, which is also the only place their behaviour is observable to
/// anyone.
/// </remarks>
public sealed class LineEndingsRuleTests
{
    private const string AttributesPath = "lf.attributes";

    private static readonly string[] SourceFiles = ["**/*.cs"];

    private readonly LineEndingsRule _rule = new();

    /// <summary>
    /// Where the requirement comes from, and what it does to a file.
    /// </summary>
    /// <remarks>
    /// The whole resolution in one table. The row with a declared carriage
    /// return over a policy asking for line feeds is the reason the attributes
    /// file is consulted first: it is a declaration the repository made about
    /// itself, and policy contradicting it would fail a file for obeying its own
    /// repository. The exempt row is why the answer has four states and not two
    /// — a path declared to have no text handling is not judged by its endings,
    /// and the policy list does not get to overrule that.
    /// </remarks>
    [Theory]
    [InlineData(null, false, "a\r\nb", RuleStatus.NotApplicable)]
    [InlineData("*.cs text eol=lf", false, "a\r\nb", RuleStatus.Failed)]
    [InlineData("*.cs text eol=lf", false, "a\nb", RuleStatus.Passed)]
    [InlineData(null, true, "a\r\nb", RuleStatus.Failed)]
    [InlineData(null, true, "a\nb", RuleStatus.Passed)]
    [InlineData("*.cs text eol=crlf", true, "a\r\nb", RuleStatus.Passed)]
    [InlineData("*.cs text eol=crlf", true, "a\nb", RuleStatus.Failed)]
    [InlineData("*.cs -text", true, "a\r\nb", RuleStatus.NotApplicable)]
    [InlineData("*.cs text", true, "a\r\nb", RuleStatus.Failed)]
    public async Task ExecuteAsync_ResolvesTheRequiredEndingFromBothSources(
        string? attributes,
        bool policyRequiresLf,
        string content,
        RuleStatus status)
    {
        var outcome = await Run(
            [Added("src/a.cs")],
            attributes,
            content,
            policyRequiresLf ? SourceFiles : []);

        outcome.Status.ShouldBe(status);
    }

    /// <summary>
    /// No attributes file still leaves the policy half of the rule running.
    /// </summary>
    /// <remarks>
    /// Returning early on a missing attributes file would switch off half the
    /// rule without a word, and it would switch it off in exactly the
    /// repositories that need it: most of them never wrote a line about
    /// endings, and those are the ones where a shell script arrives with the
    /// wrong bytes.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithNoAttributesFile_StillAppliesThePolicyList()
    {
        var outcome = await Run([Added("src/a.cs")], attributes: null, "a\r\nb", SourceFiles);

        outcome.Status.ShouldBe(RuleStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_WithTheWrongEnding_NamesBothEndingsAndTheFirstLine()
    {
        var outcome = await Run([Added("src/a.cs")], attributes: null, "a\r\nb", SourceFiles);

        var finding = outcome.Findings.Single();

        finding.Expected.ShouldBe("LF");
        finding.Actual.ShouldBe("CRLF");
        finding.Location!.RelativePath.ShouldBe("src/a.cs");
        finding.Location.Line.ShouldBe(1);
    }

    /// <summary>
    /// A file the rule declined to open is not a file that passed.
    /// </summary>
    /// <remarks>
    /// Three ways of declining, and all three answer the same way. A tick over
    /// a file nobody read is the small lie the "not applicable" status exists
    /// not to tell — and it is the one that arrives by counting a file before
    /// deciding whether to look at it.
    /// </remarks>
    [Theory]
    [InlineData(new byte[] { 0x00, 0x01, (byte)'\n' })]
    [InlineData(new byte[] { (byte)'a', (byte)'b' })]
    [InlineData(new byte[0])]
    public async Task ExecuteAsync_WithAFileItDeclinesToRead_ReportsNotApplicable(byte[] content)
    {
        var outcome = await RunBytes([Added("src/a.cs")], attributes: null, content, SourceFiles);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// A null byte outside the probed window leaves the file text as far as this
    /// rule is concerned, and that is the point of a window: the answer is at
    /// the first newline, and reading further to reclassify a file would mean
    /// reading whole assets to decide something already decided.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithANullByteBeyondTheWindow_StillJudgesTheEnding()
    {
        byte[] content = [(byte)'a', (byte)'\r', (byte)'\n', 0x00];

        var outcome = await RunBytes(
            [Added("src/a.cs")],
            attributes: null,
            content,
            SourceFiles,
            probeBytes: 3);

        outcome.Status.ShouldBe(RuleStatus.Failed);
    }

    /// <remarks>
    /// A newline as the very first byte has no byte before it to be a carriage
    /// return. Without this the index arithmetic steps off the front of the
    /// buffer, on a file that opens with a blank line — which is an ordinary
    /// file, not a strange one.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithANewlineAsTheFirstByte_ReadsItAsALineFeed()
    {
        var outcome = await Run([Added("src/a.cs")], attributes: null, "\nabc", SourceFiles);

        outcome.Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public async Task ExecuteAsync_WithOnlyDeletions_ReportsNotApplicableWithoutOpeningAnything()
    {
        var fileSystem = Workspace(null, "src/a.cs", []);

        var outcome = await _rule.ExecuteAsync(
            Context(
                changedFiles: [Deleted("src/a.cs")],
                policy: PolicyWith(Settings(SourceFiles, null)),
                fileSystem: fileSystem),
            CancellationToken.None);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
        fileSystem.DidNotReceive().OpenRead(Arg.Any<string>());
    }

    /// <remarks>
    /// The change set describes a diff, not the disk. A file deleted after the
    /// reference it was diffed against is named there and absent here, and
    /// opening it would error a perfectly ordinary commit.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAChangedFileThatIsNotOnDisk_ReportsNotApplicable()
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(false);

        var outcome = await _rule.ExecuteAsync(
            Context(
                changedFiles: [Added("src/a.cs")],
                policy: PolicyWith(Settings(SourceFiles, null)),
                fileSystem: fileSystem),
            CancellationToken.None);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoPolicyValue_ReadsTheDeclaredWindow()
    {
        var stream = new RecordingStream(Encoding.ASCII.GetBytes("a\nb"));

        await RunWithStream(stream, probeBytes: null);

        stream.LargestRequest.ShouldBe(LineEndingsRule.DefaultProbeBytes);
    }

    /// <summary>
    /// A window of zero is clamped rather than obeyed.
    /// </summary>
    /// <remarks>
    /// Without the floor, a policy of zero switches the rule off while leaving
    /// it reporting that it looked — which is the silent failure the whole set
    /// exists to prevent. Two bytes, because telling one ending from the other
    /// needs the byte before the newline.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAWindowBelowTheFloor_ClampsItAndStillExamines()
    {
        var outcome = await Run([Added("src/a.cs")], attributes: null, "\r\na", SourceFiles, probeBytes: 0);

        outcome.Status.ShouldBe(RuleStatus.Failed);
    }

    /// <remarks>
    /// A stream is entitled to return fewer bytes than it was asked for, and
    /// does over a network share. A single read taken for the whole head would
    /// produce a different verdict, intermittently, on somebody else's machine.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheStreamReturnsAShortRead_StillReadsTheWholeWindow()
    {
        var outcome = await RunWithStream(
            new RecordingStream(Encoding.ASCII.GetBytes("abcdefghij\r\nx"), chunk: 4),
            probeBytes: null);

        outcome.Status.ShouldBe(RuleStatus.Failed);
    }

    /// <summary>
    /// The attributes reader answers each question from its own entries.
    /// </summary>
    /// <remarks>
    /// The last matching line wins per attribute, not per line. The final two
    /// rows are the pair that matters: a line about the filter after a line
    /// about endings does not erase the ending, and the reverse holds for the
    /// rule next door.
    /// </remarks>
    [Theory]
    [InlineData("*.cs text eol=lf", RuleStatus.Passed)]
    [InlineData("*.cs eol=lf", RuleStatus.Passed)]
    [InlineData("*.cs text eol=lf\n*.cs -text", RuleStatus.NotApplicable)]
    [InlineData("*.cs text eol=lf\n*.cs !text", RuleStatus.NotApplicable)]
    [InlineData("*.cs text eol=lf\n*.cs binary", RuleStatus.NotApplicable)]
    [InlineData("*.cs text=auto", RuleStatus.NotApplicable)]
    [InlineData("*.cs filter=lfs", RuleStatus.NotApplicable)]
    [InlineData("* text eol=crlf\nsrc/*.cs text eol=lf", RuleStatus.Passed)]
    [InlineData("src/*.cs text eol=lf\n* text eol=crlf", RuleStatus.Failed)]
    [InlineData("*.cs text eol=lf\n*.cs filter=lfs", RuleStatus.Passed)]
    public async Task ExecuteAsync_ForEachDeclaration_ResolvesTheEndingItStates(
        string attributes,
        RuleStatus status)
    {
        var outcome = await Run([Added("src/a.cs")], attributes, "a\nb", policy: []);

        outcome.Status.ShouldBe(status);
    }

    /// <remarks>
    /// An attributes file that mentions endings nowhere leaves the rule with no
    /// requirement from that source, so it falls to the policy list — and with
    /// no policy either there is nothing to measure against.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAnAttributesFileThatMentionsNoEnding_ReportsNotApplicable()
    {
        var outcome = await Run([Added("src/a.cs")], "*.psd filter=lfs", "a\r\nb", policy: []);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoFileMatchingTheList_ReportsNotApplicable()
    {
        var outcome = await Run([Added("notes/a.md")], attributes: null, "a\r\nb", SourceFiles);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_DoesNotSwallowTheCancellation()
    {
        using var source = new CancellationTokenSource();

        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _rule.ExecuteAsync(
                Context(
                    changedFiles: [Added("src/a.cs")],
                    policy: PolicyWith(Settings(SourceFiles, null)),
                    fileSystem: Workspace(null, "src/a.cs", Encoding.ASCII.GetBytes("a\nb"))),
                source.Token));
    }

    private Task<RuleOutcome> Run(
        IReadOnlyList<ChangedFile> changed,
        string? attributes,
        string content,
        IReadOnlyList<string> policy,
        long? probeBytes = null) =>
        RunBytes(changed, attributes, Encoding.ASCII.GetBytes(content), policy, probeBytes);

    private Task<RuleOutcome> RunBytes(
        IReadOnlyList<ChangedFile> changed,
        string? attributes,
        byte[] content,
        IReadOnlyList<string> policy,
        long? probeBytes = null) =>
        _rule.ExecuteAsync(
            Context(
                changedFiles: changed,
                policy: PolicyWith(Settings(policy, probeBytes)),
                fileSystem: Workspace(attributes, "src/a.cs", content)),
            CancellationToken.None);

    private async Task<RuleOutcome> RunWithStream(RecordingStream stream, long? probeBytes)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(true);
        fileSystem.OpenRead(Arg.Any<string>()).Returns(stream);

        return await _rule.ExecuteAsync(
            Context(
                changedFiles: [Added("src/a.cs")],
                policy: PolicyWith(Settings(SourceFiles, probeBytes)),
                fileSystem: fileSystem),
            CancellationToken.None);
    }

    private static Dictionary<string, object?> Settings(IReadOnlyList<string> lf, long? probeBytes)
    {
        var settings = new Dictionary<string, object?>
        {
            ["attributesPath"] = AttributesPath,
            ["lf"] = lf.ToArray(),
        };

        if (probeBytes is { } bytes)
        {
            settings["maxProbeBytes"] = bytes;
        }

        return settings;
    }

    /// <remarks>
    /// Paths are matched by their tail, because the rule joins them with a
    /// workspace root the fixture owns and a comparison that rebuilt the join
    /// would be asserting the arrangement rather than the rule.
    /// </remarks>
    private static IFileSystem Workspace(string? attributes, string relativePath, byte[] content)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        bool IsAttributes(string path) =>
            path.Replace('\\', '/').EndsWith(AttributesPath, StringComparison.Ordinal);

        bool IsFile(string path) =>
            path.Replace('\\', '/').EndsWith(relativePath, StringComparison.Ordinal);

        fileSystem.FileExists(Arg.Any<string>()).Returns(call =>
        {
            var path = call.Arg<string>();

            return IsAttributes(path) ? attributes is not null : IsFile(path);
        });

        if (attributes is not null)
        {
            fileSystem.ReadAllTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(attributes);
        }

        fileSystem.OpenRead(Arg.Is<string>(path => Tail(path, relativePath)))
            .Returns(_ => new MemoryStream(content));

        return fileSystem;
    }

    private static bool Tail(string path, string relativePath) =>
        path.Replace('\\', '/').EndsWith(relativePath, StringComparison.Ordinal);

    /// <summary>
    /// A stream that answers in fixed-size pieces and remembers the largest
    /// buffer it was handed.
    /// </summary>
    /// <remarks>
    /// The size of that buffer is how the window the rule chose becomes
    /// observable at all: nothing else about a run says how much it decided to
    /// read.
    /// </remarks>
    private sealed class RecordingStream(byte[] content, int chunk = int.MaxValue) : MemoryStream(content)
    {
        public int LargestRequest { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            LargestRequest = Math.Max(LargestRequest, buffer.Length);

            return base.ReadAsync(buffer[..Math.Min(buffer.Length, chunk)], cancellationToken);
        }
    }
}
