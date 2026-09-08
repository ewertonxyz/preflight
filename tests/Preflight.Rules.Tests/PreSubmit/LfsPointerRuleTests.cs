namespace Preflight.Rules.Tests.PreSubmit;

using System.Text;
using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="LfsPointerRule"/>, and with it the two collaborators it
/// composes.
/// </summary>
/// <remarks>
/// Neither the attributes reader nor the pointer recogniser has a test file of
/// its own. They are internal, this repository has no <c>InternalsVisibleTo</c>
/// by decision, and a collaborator is never surface — so every branch of both
/// is driven through the rule that holds them, which is also the only place
/// their behaviour is observable to anyone.
/// </remarks>
public sealed class LfsPointerRuleTests
{
    private const string AttributesPath = "lfs.attributes";

    /// <summary>
    /// The first line of every pointer file, written out here rather than read
    /// from the rule.
    /// </summary>
    /// <remarks>
    /// A test that asked the implementation what a pointer looks like would
    /// pass whatever the implementation happened to say, including a typo. This
    /// literal is the format's, and it is the whole point of the comparison.
    /// </remarks>
    private const string Preamble = "version https://git-lfs.github.com/spec/v1";

    private static readonly byte[] PngMagic = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly LfsPointerRule _rule = new();

    /// <summary>
    /// A legitimate pointer file, as Git LFS writes one.
    /// </summary>
    private static byte[] Pointer() => Encoding.ASCII.GetBytes(
        Preamble + "\noid sha256:" + new string('a', 64) + "\nsize 12345\n");

    private static byte[] RealBlob() => [.. PngMagic, .. Enumerable.Repeat((byte)0x42, 60)];

    private Task<RuleOutcome> Run(
        IReadOnlyList<ChangedFile> changed,
        string? attributes,
        IReadOnlyDictionary<string, byte[]>? blobs = null,
        IReadOnlyDictionary<string, object?>? policy = null,
        IFileSystem? fileSystem = null) =>
        _rule.ExecuteAsync(
            Context(
                changedFiles: changed,
                policy: PolicyWith(Settings(policy)),
                fileSystem: fileSystem ?? Workspace(attributes, blobs)),
            CancellationToken.None);

    private static Dictionary<string, object?> Settings(IReadOnlyDictionary<string, object?>? extra)
    {
        var settings = new Dictionary<string, object?> { ["attributesPath"] = AttributesPath };

        foreach (var pair in extra ?? new Dictionary<string, object?>())
        {
            settings[pair.Key] = pair.Value;
        }

        return settings;
    }

    /// <summary>
    /// A workspace holding an attributes file and some blobs.
    /// </summary>
    /// <remarks>
    /// Paths are matched by their tail rather than in full, because the rule
    /// combines them with a workspace root the fixture owns and a test that
    /// rebuilt that combination would be asserting the arrange rather than the
    /// rule.
    /// </remarks>
    private static IFileSystem Workspace(string? attributes, IReadOnlyDictionary<string, byte[]>? blobs)
    {
        var fileSystem = Substitute.For<IFileSystem>();
        var files = blobs ?? new Dictionary<string, byte[]>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(call =>
        {
            var path = Normalise(call.Arg<string>());

            return (attributes is not null && path.EndsWith(AttributesPath, StringComparison.Ordinal))
                || files.Keys.Any(blob => path.EndsWith(blob, StringComparison.Ordinal));
        });

        if (attributes is not null)
        {
            fileSystem.ReadAllTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(attributes);
        }

        fileSystem.OpenRead(Arg.Any<string>()).Returns(call =>
        {
            var path = Normalise(call.Arg<string>());
            var match = files.FirstOrDefault(blob => path.EndsWith(blob.Key, StringComparison.Ordinal));

            return match.Value is null
                ? throw new FileNotFoundException(path)
                : (Stream)new MemoryStream(match.Value);
        });

        return fileSystem;
    }

    private static string Normalise(string path) => path.Replace('\\', '/');

    [Fact]
    public async Task ExecuteAsync_WithNoAttributesFile_IsNotApplicable()
    {
        var outcome = await Run([Added("art/a.psd")], attributes: null);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
        outcome.Findings.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithNoLfsPatterns_IsNotApplicable()
    {
        (await Run([Added("art/a.psd")], "* text=auto")).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// <c>Passed</c> here would claim that files were opened and found to be
    /// pointers when none was opened at all.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithNoChangedFileMatchingAnLfsPattern_IsNotApplicable()
    {
        var outcome = await Run([Added("src/a.cs")], "*.psd filter=lfs");

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
        outcome.Status.ShouldNotBe(RuleStatus.Passed);
    }

    [Fact]
    public async Task ExecuteAsync_WithOnlyDeletedLfsFiles_IsNotApplicable()
    {
        var fileSystem = Workspace("*.psd filter=lfs", blobs: null);

        var outcome = await _rule.ExecuteAsync(
            Context(
                changedFiles: [Deleted("art/a.psd")],
                policy: PolicyWith(Settings(null)),
                fileSystem: fileSystem),
            CancellationToken.None);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
        fileSystem.DidNotReceive().OpenRead(Arg.Any<string>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenAMatchedFileIsAPointer_Passes()
    {
        var outcome = await Run(
            [Added("art/a.psd")],
            "*.psd filter=lfs",
            new Dictionary<string, byte[]> { ["art/a.psd"] = Pointer() });

        outcome.Status.ShouldBe(RuleStatus.Passed);
        outcome.Findings.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenAMatchedFileIsARealBlob_FailsNamingThePath()
    {
        var outcome = await Run(
            [Added("art/a.psd")],
            "*.psd filter=lfs",
            new Dictionary<string, byte[]> { ["art/a.psd"] = RealBlob() });

        outcome.Status.ShouldBe(RuleStatus.Failed);

        var finding = outcome.Findings.ShouldHaveSingleItem();

        finding.Location.ShouldNotBeNull().RelativePath.ShouldBe("art/a.psd");
        finding.Remediation.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The report names the path and never the bytes.
    /// </summary>
    /// <remarks>
    /// This rule opens files whose whole point is that they are content, and
    /// everything it writes reaches the console, the build log and the run's
    /// stored history. A single quoted line would publish whatever happened to
    /// be at the start of the file to everyone who can read a build.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_ReportsThePathAndNeverTheContent()
    {
        const string Marker = "SECRETMARKERTEXT";

        var outcome = await Run(
            [Added("art/a.psd")],
            "*.psd filter=lfs",
            new Dictionary<string, byte[]> { ["art/a.psd"] = Encoding.ASCII.GetBytes(Marker + new string('x', 40)) });

        outcome.Status.ShouldBe(RuleStatus.Failed);

        foreach (var finding in outcome.Findings)
        {
            var fields = new[] { finding.Message, finding.Expected, finding.Actual, finding.Remediation };

            fields.ShouldAllBe(field => field == null || !field.Contains(Marker, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ExecuteAsync_ForARename_ExaminesTheNewPathAndNotTheOld()
    {
        var fileSystem = Workspace(
            "*.psd filter=lfs",
            new Dictionary<string, byte[]> { ["art/new.psd"] = Pointer() });

        await _rule.ExecuteAsync(
            Context(
                changedFiles: [Renamed("art/old.psd", "art/new.psd")],
                policy: PolicyWith(Settings(null)),
                fileSystem: fileSystem),
            CancellationToken.None);

        fileSystem.Received(1).OpenRead(Arg.Is<string>(path => Normalise(path).EndsWith("art/new.psd", StringComparison.Ordinal)));
        fileSystem.DidNotReceive().OpenRead(Arg.Is<string>(path => Normalise(path).EndsWith("art/old.psd", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A matched file that is not on disk is not an errored rule.
    /// </summary>
    /// <remarks>
    /// A change set naming a file the working tree no longer holds is ordinary,
    /// and opening it would throw out of the rule and be reported as
    /// <c>Errored</c> — the tool blaming itself for a perfectly normal commit.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenAMatchedFileIsNotOnDisk_IsNotErrored()
    {
        var outcome = await Run([Added("art/a.psd")], "*.psd filter=lfs");

        outcome.Status.ShouldNotBe(RuleStatus.Errored);
        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_ReadsTheAttributesPathFromPolicy()
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(true);
        fileSystem.ReadAllTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("* text=auto");

        await _rule.ExecuteAsync(
            Context(
                changedFiles: [Added("art/a.psd")],
                policy: PolicyWith("attributesPath", "meta/lfs.attributes"),
                fileSystem: fileSystem),
            CancellationToken.None);

        await fileSystem.Received().ReadAllTextAsync(
            Arg.Is<string>(path => Normalise(path).EndsWith("meta/lfs.attributes", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    /// <remarks>
    /// The cap exists to avoid reading a very large asset, not to decide the
    /// question. Honouring a cap below the preamble would report every clean
    /// commit as broken.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithMaxProbeBytesBelowThePreamble_StillReadsThePointer()
    {
        var outcome = await Run(
            [Added("art/a.psd")],
            "*.psd filter=lfs",
            new Dictionary<string, byte[]> { ["art/a.psd"] = Pointer() },
            new Dictionary<string, object?> { ["maxProbeBytes"] = 8L });

        outcome.Status.ShouldBe(RuleStatus.Passed);
    }

    /// <remarks>
    /// A stream is entitled to return fewer bytes than were asked for, and does
    /// over a network share or a filter driver. A single read whose result was
    /// treated as the whole head would call a legitimate pointer a blob,
    /// intermittently, on somebody else's machine.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheStreamReturnsAShortRead_StillReadsThePointer()
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(true);
        fileSystem.ReadAllTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("*.psd filter=lfs");
        fileSystem.OpenRead(Arg.Any<string>()).Returns(_ => new DribblingStream(Pointer(), 10));

        var outcome = await _rule.ExecuteAsync(
            Context(
                changedFiles: [Added("art/a.psd")],
                policy: PolicyWith(Settings(null)),
                fileSystem: fileSystem),
            CancellationToken.None);

        outcome.Status.ShouldBe(RuleStatus.Passed);
    }

    /// <summary>
    /// The preamble, byte by byte.
    /// </summary>
    /// <remarks>
    /// Every row here is a file that looks like a pointer to a human and is not
    /// one to Git. A byte order mark in front of the text is the one that
    /// actually happens, because an editor that "helpfully" saved the pointer
    /// adds it.
    /// </remarks>
    [Theory]
    [InlineData("short", false)]
    [InlineData("exact-preamble", true)]
    [InlineData("preamble-and-body", true)]
    [InlineData("one-byte-short", false)]
    [InlineData("bom-then-preamble", false)]
    [InlineData("uppercase-preamble", false)]
    public async Task ExecuteAsync_RecognisesThePointerPreamble(string shape, bool recognised)
    {
        var outcome = await Run(
            [Added("art/a.psd")],
            "*.psd filter=lfs",
            new Dictionary<string, byte[]> { ["art/a.psd"] = Shaped(shape) });

        outcome.Status.ShouldBe(recognised ? RuleStatus.Passed : RuleStatus.Failed);
    }

    private static byte[] Shaped(string shape) => shape switch
    {
        "short" => Encoding.ASCII.GetBytes("version"),
        "exact-preamble" => Encoding.ASCII.GetBytes(Preamble),
        "preamble-and-body" => Pointer(),
        "one-byte-short" => Encoding.ASCII.GetBytes(Preamble[..^1]),
        "bom-then-preamble" => [.. Encoding.UTF8.GetPreamble(), .. Encoding.ASCII.GetBytes(Preamble)],
        _ => Encoding.ASCII.GetBytes(Preamble.ToUpperInvariant()),
    };

    /// <summary>
    /// The one place the two glob dialects genuinely differ.
    /// </summary>
    /// <remarks>
    /// An attributes pattern with no slash in it applies at every depth, which
    /// is the opposite of what the policy dialect means by the same text. The
    /// common case — an extension applied to the whole tree — is written
    /// exactly that way, so reusing the policy dialect unchanged would silently
    /// stop matching almost everything.
    /// </remarks>
    [Theory]
    [InlineData("*.psd", "a.psd", true)]
    [InlineData("*.psd", "art/deep/a.psd", true)]
    [InlineData("art/*.psd", "art/a.psd", true)]
    [InlineData("art/*.psd", "art/deep/a.psd", false)]
    [InlineData("/root.psd", "root.psd", true)]
    [InlineData("/root.psd", "a/root.psd", false)]
    [InlineData("art/**/*.psd", "art/x/y/a.psd", true)]
    public async Task ExecuteAsync_AppliesTheGitAttributesDialect(string pattern, string path, bool matched)
    {
        var outcome = await Run(
            [Added(path)],
            $"{pattern} filter=lfs",
            new Dictionary<string, byte[]> { [path] = RealBlob() });

        outcome.Status.ShouldBe(matched ? RuleStatus.Failed : RuleStatus.NotApplicable);
    }

    /// <summary>
    /// The filter attribute is matched as a token, never as a substring.
    /// </summary>
    /// <remarks>
    /// A <c>Contains("filter=lfs")</c> would accept <c>filter=lfsx</c>, which is
    /// somebody else's filter entirely, and would miss none of the ways Git has
    /// of turning an attribute off. Both directions of that mistake end in a
    /// rule that reports on the wrong files.
    /// </remarks>
    [Theory]
    [InlineData("filter=lfs", true)]
    [InlineData("filter=lfs diff=lfs merge=lfs -text", true)]
    [InlineData("filter=lfsx", false)]
    [InlineData("-filter", false)]
    [InlineData("!filter", false)]
    [InlineData("text", false)]
    [InlineData("# filter=lfs", false)]
    [InlineData("[attr]binary filter=lfs", false)]
    [InlineData("filter=LFS", false)]
    public async Task ExecuteAsync_RecognisesTheLfsFilterAttribute(string attributes, bool lfs)
    {
        var line = attributes.StartsWith('#') || attributes.StartsWith('[')
            ? attributes
            : $"*.psd {attributes}";

        var outcome = await Run(
            [Added("art/a.psd")],
            line,
            new Dictionary<string, byte[]> { ["art/a.psd"] = RealBlob() });

        outcome.Status.ShouldBe(lfs ? RuleStatus.Failed : RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// Git resolves attributes last-rule-wins, so a broad line sending every
    /// <c>.psd</c> to LFS followed by a narrow one taking a vendored directory
    /// back out means those files are deliberately not in LFS. Reporting them
    /// would fail a commit the repository explicitly exempted.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithALaterPatternUnsettingTheFilter_DoesNotTreatItAsLfs()
    {
        var outcome = await Run(
            [Added("vendor/a.psd")],
            "*.psd filter=lfs\nvendor/*.psd -filter",
            new Dictionary<string, byte[]> { ["vendor/a.psd"] = RealBlob() });

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// A pattern on its own says nothing about any attribute, so it is not an
    /// entry at all. Recording it as one would give it a filter state nobody
    /// wrote, and last-rule-wins would then let it override the line above it.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_IgnoresALineThatNamesAPatternAndNoAttribute()
    {
        var outcome = await Run(
            [Added("art/a.psd")],
            "*.psd filter=lfs\n*.psd",
            new Dictionary<string, byte[]> { ["art/a.psd"] = RealBlob() });

        outcome.Status.ShouldBe(RuleStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresCommentsBlankLinesAndMacroLines()
    {
        var attributes = string.Join(
            '\n',
            "# assets live in LFS",
            string.Empty,
            "[attr]binary -text -diff",
            "*.psd filter=lfs");

        var outcome = await Run(
            [Added("art/a.psd")],
            attributes,
            new Dictionary<string, byte[]> { ["art/a.psd"] = RealBlob() });

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem();
    }

    /// <remarks>
    /// The volume a real pre-submit run sees, in the shape the forbidden-path
    /// rule's own volume test already uses. The planted file is written
    /// literally so that a reader of a failure knows which one it was; the rest
    /// exist only to be counted.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_OverTenThousandChangedFiles_FindsThePlantedBlob()
    {
        var changed = Enumerable.Range(0, 9_999)
            .Select(index => Added($"src/module-{index}/file.cs"))
            .Append(Added("art/planted.psd"))
            .ToArray();

        var outcome = await Run(
            changed,
            "*.psd filter=lfs",
            new Dictionary<string, byte[]> { ["art/planted.psd"] = RealBlob() });

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Location.ShouldNotBeNull().RelativePath.ShouldBe("art/planted.psd");
    }

    [Fact]
    public async Task ExecuteAsync_WithACancelledToken_StopsRatherThanFinishing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _rule.ExecuteAsync(
                Context(
                    changedFiles: [Added("art/a.psd")],
                    policy: PolicyWith(Settings(null)),
                    fileSystem: Workspace(
                        "*.psd filter=lfs",
                        new Dictionary<string, byte[]> { ["art/a.psd"] = Pointer() })),
                cancellation.Token));
    }

    /// <summary>
    /// A stream that hands back less than it was asked for.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than substituted because the behaviour under test is
    /// the sequence of returns from <see cref="Read(byte[], int, int)"/>, and a
    /// substitute expressing that reads as a puzzle.
    /// </remarks>
    private sealed class DribblingStream : Stream
    {
        private readonly byte[] _content;

        private readonly int _chunk;

        private int _position;

        public DribblingStream(byte[] content, int chunk)
        {
            _content = content;
            _chunk = chunk;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _content.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = Math.Min(Math.Min(_chunk, count), _content.Length - _position);

            Array.Copy(_content, _position, buffer, offset, take);
            _position += take;

            return take;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
