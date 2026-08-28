namespace Preflight.Cli;

using System.Text;

/// <summary>
/// What the console this run is writing to can actually do.
/// </summary>
/// <remarks>
/// <para>
/// Four static members of <see cref="Console"/> stand between the console
/// reporter and a test, and each one fails differently. <c>OutputEncoding</c>
/// and <c>SetOut</c> are process-global with no teardown, which makes any test
/// that touches them visible to every other test class xUnit is running in
/// parallel. <c>IsOutputRedirected</c> is permanently <see langword="true"/>
/// under a test runner, so the coloured branch is the one no test could ever
/// reach by accident. And <c>WindowWidth</c> <em>throws</em> when there is no
/// console attached — which is every CI agent and every coverage run.
/// </para>
/// <para>
/// All four need a branch, and the tests need golden files taken at a fixed
/// terminal width. One record, built once in <c>Program</c> from the real
/// console and injected everywhere else, is the only arrangement in which both
/// are testable.
/// </para>
/// </remarks>
/// <param name="Output">Where the report is written.</param>
/// <param name="Encoding">
/// The encoding <paramref name="Output"/> will be rendered in. Decides whether
/// the unicode glyphs survive.
/// </param>
/// <param name="IsInteractive">
/// <see langword="false"/> when output is redirected: no colour
/// outside a terminal, so a CI log is not polluted with escape sequences.
/// </param>
/// <param name="IsInputInteractive">
/// <see langword="false"/> when input is redirected. The other half of
/// <paramref name="IsInteractive"/>, and a separate member rather than the same
/// one read twice: that one is about stdout, this one is about stdin, and
/// <c>echo 1 | preflight pipeline use</c> has an interactive stdout with a
/// redirected stdin. A picker reads stdin, so it is this member it must ask.
/// </param>
/// <param name="Width">Terminal width, for aligning the report.</param>
public sealed record ConsoleCapabilities(
    TextWriter Output,
    Encoding Encoding,
    bool IsInteractive,
    bool IsInputInteractive,
    int Width)
{
    /// <summary>
    /// The width used when the real console will not say — every CI agent, and
    /// every run under a test host.
    /// </summary>
    /// <remarks>
    /// 100 columns rather than 80: the report puts a glyph, a rule id and a
    /// duration on one line, and a rule id is at least three dotted segments. A
    /// fixed number beats a guess that throws.
    /// </remarks>
    public const int DefaultWidth = 100;

    /// <summary>
    /// Reads the real console, tolerating the parts of it that are not there.
    /// </summary>
    public static ConsoleCapabilities Detect() => new(
        Console.Out,
        Console.OutputEncoding,
        !Console.IsOutputRedirected,
        !Console.IsInputRedirected,
        DetectWidth());

    /// <summary>
    /// Whether this console can render every glyph in
    /// <paramref name="glyphs"/>.
    /// </summary>
    /// <remarks>
    /// Round-tripped rather than inferred from the encoding's name. An encoding
    /// that cannot represent a character does not fail — it substitutes,
    /// usually with <c>?</c>, and a validation report printed as a row of
    /// question marks is a useless report. Asking the encoding to prove it can
    /// carry the text is the only check that catches every codepage without
    /// enumerating them.
    /// </remarks>
    public bool CanRender(IEnumerable<string> glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);

        foreach (var glyph in glyphs)
        {
            if (!string.Equals(Encoding.GetString(Encoding.GetBytes(glyph)), glyph, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int DetectWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch (IOException)
        {
            // No console attached. Not exceptional — it is the normal state of
            // a build agent, which is precisely where the report has to remain
            // readable.
            return DefaultWidth;
        }
    }
}
