namespace Sample.Production.Rules;

using System.Buffers.Binary;
using Preflight.Abstractions.Services;

/// <summary>
/// The pixel dimensions of a texture.
/// </summary>
public sealed record TextureSize(int Width, int Height);

/// <summary>
/// Decides what counts as a texture, and reads how big one is.
/// </summary>
/// <remarks>
/// <para>
/// The part of a production rule that is about the production's own file
/// formats rather than about Preflight. It is separate from the rule for the
/// reason every rule in this repository is separate from the engine: the rule
/// then has one thing to test — the policy limit and the verdict — and this has
/// one thing to test, which is bytes.
/// </para>
/// <para>
/// PNG only, and that is the honest scope rather than a stub. A real production
/// adds TGA, DDS and whatever its artists ship, each with a header layout of its
/// own; what none of them changes is the shape here, which is that the probe
/// returns <see langword="null"/> when it cannot answer instead of guessing.
/// A texture the probe cannot read is not a texture that violates the limit.
/// </para>
/// </remarks>
public static class TextureProbe
{
    /// <summary>The eight bytes that open every PNG.</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Signature, chunk length, "IHDR", width, height — the prefix that has to
    /// be present before either dimension can be read.
    /// </summary>
    private const int HeaderBytes = 24;

    private const int WidthOffset = 16;

    private const int HeightOffset = 20;

    /// <summary>
    /// Whether <paramref name="relativePath"/> is a file this rule judges.
    /// </summary>
    /// <remarks>
    /// By extension, and case-insensitively, because a commit brings
    /// <c>.PNG</c> as often as <c>.png</c> and a rule that judged only one of
    /// them would pass a texture into the build by spelling.
    /// </remarks>
    public static bool IsTexture(string relativePath) =>
        Path.GetExtension(relativePath).Equals(".png", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The dimensions of the texture at <paramref name="path"/>, or
    /// <see langword="null"/> if they cannot be read.
    /// </summary>
    /// <remarks>
    /// Through <see cref="IFileSystem"/>, never <c>File.OpenRead</c>. Section
    /// 11.3 lists that as one of the three things the example is really
    /// teaching: it is what makes the rule unit-testable, and it is the only
    /// access a rule is given.
    /// </remarks>
    public static async Task<TextureSize?> TryReadDimensionsAsync(
        IFileSystem fileSystem,
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var header = new byte[HeaderBytes];

        await using var stream = fileSystem.OpenRead(path);

        // ReadAtLeast rather than a single ReadAsync: a stream is allowed to
        // return fewer bytes than asked for without being at its end, and code
        // that treats a short read as a short file works until the day the file
        // arrives over a network share.
        var read = await stream.ReadAtLeastAsync(
            header,
            HeaderBytes,
            throwOnEndOfStream: false,
            cancellationToken);

        if (read < HeaderBytes || !header.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            return null;
        }

        return new TextureSize(
            BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(WidthOffset)),
            BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(HeightOffset)));
    }
}
