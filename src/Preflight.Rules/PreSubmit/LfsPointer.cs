namespace Preflight.Rules;

using System.Text;

/// <summary>
/// Recognises the preamble of a Git LFS pointer file in a buffer of bytes.
/// </summary>
/// <remarks>
/// <para>
/// Its own type rather than a method on the rule, because "what a pointer looks
/// like on disk" is a different reason to change than "what the report says
/// about a file that is not one". The first moves when the pointer format does;
/// the second moves when somebody rewrites a remediation.
/// </para>
/// <para>
/// It reads bytes and never text. The file being examined is by definition
/// something that may be a binary asset, and decoding one as UTF-8 to look at
/// its first line either throws or produces replacement characters — neither of
/// which is a fact about whether it is a pointer.
/// </para>
/// </remarks>
internal static class LfsPointer
{
    /// <summary>
    /// The first bytes of every pointer file, and nothing else.
    /// </summary>
    /// <remarks>
    /// The version line of the pointer specification, ASCII, without its
    /// trailing newline. Compared exactly and case-sensitively: the format
    /// writes it one way, so anything that differs — a byte order mark in
    /// front of it, a different case — is a file that merely resembles a
    /// pointer, and treating it as one would pass a real blob.
    /// </remarks>
    public const string Preamble = "version https://git-lfs.github.com/spec/v1";

    /// <summary>
    /// How many bytes have to be read before the question can be answered.
    /// </summary>
    public static int PreambleLength => Encoding.ASCII.GetByteCount(Preamble);

    /// <summary>
    /// Whether <paramref name="head"/> begins with the pointer preamble.
    /// </summary>
    /// <param name="head">The opening bytes of the file, however many were read.</param>
    /// <returns>
    /// <see langword="true"/> when the bytes start with the preamble exactly.
    /// </returns>
    public static bool Recognises(ReadOnlySpan<byte> head) =>
        head.Length >= PreambleLength
        && head[..PreambleLength].SequenceEqual(Encoding.ASCII.GetBytes(Preamble));
}
