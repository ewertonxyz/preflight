namespace Preflight.Rules.Tests.Samples;

using NSubstitute;
using Preflight.Abstractions;
using Sample.Production.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// The worked example of a production's own rule, held to the contract it
/// teaches.
/// </summary>
/// <remarks>
/// <para>
/// The sample is documentation that compiles, and these tests exist because
/// documentation that compiles can still be wrong. It teaches three things the
/// example is really teaching — reading through
/// <see cref="IFileSystem"/>, checking the cancellation token inside the loop,
/// and returning <c>NotApplicable</c> rather than <c>Passed</c> when nothing
/// was examined — and each of them is something a reader will copy into a real
/// production rule.
/// </para>
/// <para>
/// The admission criterion for a built-in rule does not apply: it governs a
/// rule shipped <em>inside</em> the tool, and the whole point of this one is
/// that it lives outside it. What does apply is the invariant that a failing
/// rule always says how to fix it, because that is the part being copied.
/// </para>
/// </remarks>
public sealed class SampleTextureDimensionRuleTests
{
    private static readonly TextureDimensionRule Rule = new();

    /// <remarks>
    /// A commit touching only source files gives the rule nothing to measure. A
    /// tick here would claim a check that never happened, which is what is a
    /// small lie that erodes trust in the whole report.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithNoTextureAmongTheChangedFiles_IsNotApplicable()
    {
        var outcome = await Rule.ExecuteAsync(
            Context(changedFiles: [Modified("src/Program.cs")]),
            TestContext.Current.CancellationToken);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithATextureWithinTheLimit_Passes()
    {
        var outcome = await Rule.ExecuteAsync(ContextFor("art/atlas.png", 2048, 2048), Cancellation);

        outcome.Status.ShouldBe(RuleStatus.Passed);
    }

    /// <remarks>
    /// <c>Expected</c> and <c>Actual</c> are separate fields rather than prose
    /// inside the message, and <c>Remediation</c> is not optional in practice:
    /// a rule that rejects a commit without saying what to do about it is the
    /// thing no rule may ship.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithATextureOverTheLimit_FailsSayingExpectedActualAndRemediation()
    {
        var outcome = await Rule.ExecuteAsync(ContextFor("art/atlas.png", 8192, 8192), Cancellation);

        outcome.Status.ShouldBe(RuleStatus.Failed);

        var finding = outcome.Findings.ShouldHaveSingleItem();

        finding.Location!.RelativePath.ShouldBe("art/atlas.png");
        finding.Expected.ShouldBe("<= 4096px");
        finding.Actual.ShouldBe("8192x8192px");
        finding.Remediation.ShouldNotBeNullOrWhiteSpace();
    }

    /// <remarks>
    /// The whole point of the example. The same DLL serves a production that
    /// allows 8192 and one that allows 2048, and nothing in the rule knows
    /// which it is running for.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAMaxDimensionInPolicy_UsesItInsteadOfTheDefault()
    {
        var outcome = await Rule.ExecuteAsync(
            ContextFor("art/atlas.png", 2048, 2048, PolicyWith("maxDimension", 1024)),
            Cancellation);

        outcome.Findings.ShouldHaveSingleItem().Expected.ShouldBe("<= 1024px");
    }

    /// <remarks>
    /// A file the probe cannot read is not a file that broke the limit. A rule
    /// that failed a commit over its own inability to parse something would be
    /// rejecting work for a defect in itself — and this is the branch a reader
    /// copies, so it is the branch that has to be right.
    /// </remarks>
    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    [InlineData(new byte[0])]
    public async Task ExecuteAsync_WithATextureItCannotRead_DoesNotFail(byte[] content)
    {
        var outcome = await Rule.ExecuteAsync(ContextFor("art/atlas.png", content), Cancellation);

        outcome.Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public async Task ExecuteAsync_WithAFileThatIsNotAPng_IgnoresIt()
    {
        var outcome = await Rule.ExecuteAsync(
            Context(changedFiles: [Modified("art/atlas.tga")]),
            Cancellation);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// Rename is the case a plugin author gets wrong. A deleted texture has
    /// nothing left to measure, and a renamed one is judged by where it is now
    /// — <c>PreviousRelativePath</c> names a file that no longer exists.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithADeletedTextureAndARenamedOne_JudgesTheRenamedOneByItsNewPath()
    {
        var fileSystem = Substitute.For<IFileSystem>();

        GivenPng(fileSystem, "art/new.png", 8192, 8192);

        var outcome = await Rule.ExecuteAsync(
            Context(
                changedFiles: [Deleted("art/gone.png"), Renamed("art/old.png", "art/new.png")],
                fileSystem: fileSystem),
            Cancellation);

        outcome.Findings.ShouldHaveSingleItem().Location!.RelativePath.ShouldBe("art/new.png");
    }

    /// <remarks>
    /// One of the three things the example teaches: a loop over a thousand
    /// textures that never checks the token cannot be stopped, and the engine's
    /// timeout becomes a promise it cannot keep.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenCancelled_Throws()
    {
        using var cancelled = new CancellationTokenSource();

        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => Rule.ExecuteAsync(ContextFor("art/atlas.png", 8192, 8192), cancelled.Token));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static RuleContext ContextFor(
        string relativePath,
        int width,
        int height,
        IPolicyReader? policy = null)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        GivenPng(fileSystem, relativePath, width, height);

        return Context(
            changedFiles: [Modified(relativePath)],
            policy: policy,
            fileSystem: fileSystem);
    }

    private static RuleContext ContextFor(string relativePath, byte[] content)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.OpenRead(Path.Combine(WorkspaceRoot.FullName, relativePath))
            .Returns(_ => new MemoryStream(content));

        return Context(changedFiles: [Modified(relativePath)], fileSystem: fileSystem);
    }

    private static void GivenPng(IFileSystem fileSystem, string relativePath, int width, int height) =>
        fileSystem.OpenRead(Path.Combine(WorkspaceRoot.FullName, relativePath))
            .Returns(_ => new MemoryStream(Png(width, height)));

    /// <summary>
    /// The first 24 bytes of a PNG: signature, chunk length, <c>IHDR</c>,
    /// width, height.
    /// </summary>
    /// <remarks>
    /// Built here rather than committed as a binary fixture. Two dimensions are
    /// the only thing the probe reads, and a real PNG in the repository would
    /// be a file nobody can review holding a number nobody can see.
    /// </remarks>
    private static byte[] Png(int width, int height)
    {
        var bytes = new byte[24];

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        signature.CopyTo(bytes);
        System.Text.Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes, 12);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);

        return bytes;
    }
}
