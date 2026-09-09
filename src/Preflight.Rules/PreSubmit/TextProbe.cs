namespace Preflight.Rules;

using System.Text;
using Preflight.Abstractions.Rules;

/// <summary>
/// What the opening bytes of a file say about it: whether it is binary, and
/// which line ending it starts with.
/// </summary>
/// <param name="IsBinary">Whether a null byte appeared in the window read.</param>
/// <param name="FirstEnding">The first line ending in that window.</param>
/// <remarks>
/// <para>
/// One reader for three rules that make the same opening read and draw
/// different conclusions from it. Two windows would let one file be binary for
/// one question and text for another, and the reader of the report would be
/// handed two answers about one file.
/// </para>
/// <para>
/// A null byte is what decides "binary", which is the heuristic the version
/// control client itself applies. A list of extensions was the alternative, and
/// it is never complete — worse, it would make the tool disagree about a file
/// with the system that produced it.
/// </para>
/// <para>
/// A third question about the head of a file belongs here as another member,
/// never as another <c>if</c> in a rule: the rules ask this type what a file is
/// and do not know how it decides.
/// </para>
/// </remarks>
internal sealed record TextProbe(bool IsBinary, LineEnding FirstEnding)
{
    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> bytes from the start of a
    /// file.
    /// </summary>
    /// <param name="context">The rule's context, for the file system.</param>
    /// <param name="path">The absolute path to open.</param>
    /// <param name="maxBytes">How much to read at most.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>What was actually read, which may be shorter.</returns>
    /// <remarks>
    /// Reads in a loop rather than once. A stream is entitled to return fewer
    /// bytes than were asked for, and does over a network share or behind a
    /// filter driver — so a single read whose result was taken for the whole
    /// head would produce a different verdict, intermittently, on somebody
    /// else's machine. The loop lives here rather than in each rule because a
    /// second copy is a second place the short read can come back and only one
    /// of them would have a test.
    /// </remarks>
    public static async Task<byte[]> ReadHeadAsync(
        RuleContext context,
        string path,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await using var stream = context.FileSystem.OpenRead(path);

        var head = new byte[maxBytes];
        var filled = 0;

        while (filled < head.Length)
        {
            var read = await stream.ReadAsync(head.AsMemory(filled), cancellationToken);

            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return filled == head.Length ? head : head[..filled];
    }

    /// <summary>
    /// Reads a file's bytes as text.
    /// </summary>
    /// <param name="bytes">Everything that was read.</param>
    /// <returns>The text, without a leading byte-order mark.</returns>
    /// <remarks>
    /// The mark is consumed rather than decoded. A file edited or conflicted on
    /// Windows opens with one, and left in place it becomes an invisible first
    /// character: it shifts every offset by one, defeats an expression anchored
    /// at the start of the file, and sits in front of a conflict marker on the
    /// first line — so a rule looking for one would go quiet on the single most
    /// common shape of the thing it exists to catch.
    ///
    /// Here rather than in each rule that reads a whole file, because both of
    /// them want the same thing for the same reason, and two copies are two
    /// places the mark can come back with only one of them tested.
    /// </remarks>
    public static string DecodeText(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return Encoding.UTF8.GetString(
            bytes.Length >= Utf8Bom.Length && bytes.AsSpan(0, Utf8Bom.Length).SequenceEqual(Utf8Bom)
                ? bytes.AsSpan(Utf8Bom.Length)
                : bytes);
    }

    /// <summary>
    /// Reads what a window of opening bytes says.
    /// </summary>
    /// <param name="head">The opening bytes of a file.</param>
    /// <returns>Whether it is binary, and the line ending it opens with.</returns>
    public static TextProbe Of(ReadOnlySpan<byte> head)
    {
        var newline = head.IndexOf(Lf);

        return new TextProbe(head.IndexOf(Nul) >= 0, EndingAt(head, newline));
    }

    /// <remarks>
    /// A newline at the very first byte is a line feed, because there is no
    /// byte before it to be a carriage return. Without this the index arithmetic
    /// steps off the front of the buffer, on a file that starts with a blank
    /// line — which is an ordinary file, not a strange one.
    /// </remarks>
    private static LineEnding EndingAt(ReadOnlySpan<byte> head, int newline) => newline switch
    {
        < 0 => LineEnding.None,
        0 => LineEnding.Lf,
        _ => head[newline - 1] == Cr ? LineEnding.Crlf : LineEnding.Lf,
    };

    /// <summary>
    /// The three bytes a byte-order mark is written as in this encoding.
    /// </summary>
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private const byte Nul = 0x00;

    private const byte Cr = (byte)'\r';

    private const byte Lf = (byte)'\n';
}
