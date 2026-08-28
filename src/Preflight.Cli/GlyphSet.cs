namespace Preflight.Cli;

using Preflight.Abstractions.Model;

/// <summary>
/// The status glyphs, in one of the two documented variants.
/// </summary>
/// <remarks>
/// <para>
/// Written as escapes rather than as the characters themselves. The bytes a
/// literal <c>"\u2713"</c> compiles to depend on the encoding of the source
/// file, and a file re-saved by an editor with a different default would change
/// what the tool prints without changing what the diff shows. An escape cannot.
/// </para>
/// <para>
/// The ASCII variant is not a degraded fallback. Several Windows build agents
/// default to an encoding that cannot represent the unicode set, and calls a
/// report printed as a row of question marks useless — so the variant is chosen
/// automatically as well as by <c>--no-unicode</c>.
/// </para>
/// </remarks>
public sealed class GlyphSet
{
    private readonly IReadOnlyDictionary<RuleStatus, string> _glyphs;

    private GlyphSet(
        IReadOnlyDictionary<RuleStatus, string> glyphs,
        string separator,
        string arrow,
        string absent,
        bool isUnicode)
    {
        _glyphs = glyphs;
        Separator = separator;
        Arrow = arrow;
        Absent = absent;
        IsUnicode = isUnicode;
    }

    /// <summary>
    /// What separates the fields of the header line.
    /// </summary>
    /// <remarks>
    /// Part of the variant, not a constant, and the reason is the whole point
    /// of the ASCII variant existing. A console that cannot carry U+2713 cannot
    /// carry an em dash either, so a report that swapped the glyphs and kept
    /// the punctuation would still print its header as a row of question marks
    /// — half a fix, on exactly the build agents that default to that encoding.
    /// </remarks>
    public string Separator { get; }

    /// <summary>
    /// What joins the policy files of the chain.
    /// </summary>
    public string Arrow { get; }

    /// <summary>
    /// What stands in for a value the sample cannot support.
    /// </summary>
    /// <remarks>
    /// The report prints a dash where a percentile has too few observations to
    /// be honest about, and that dash is an em dash — exactly the character
    /// several Windows build agents render as a question mark. A report whose
    /// "not enough data" marker is unreadable has replaced a missing number
    /// with a wrong-looking one.
    /// </remarks>
    public string Absent { get; }

    /// <summary>The variant using unicode glyphs.</summary>
    public static GlyphSet Unicode { get; } = new(
        new Dictionary<RuleStatus, string>
        {
            [RuleStatus.Passed] = "\u2713",
            [RuleStatus.Warning] = "!",
            [RuleStatus.Failed] = "\u2717",
            [RuleStatus.Skipped] = "\u2298",
            [RuleStatus.NotApplicable] = "\u00b7",
            [RuleStatus.Errored] = "\u2a02",
        },
        separator: "\u2014",
        arrow: "\u2192",
        absent: "\u2014",
        isUnicode: true);

    /// <summary>The variant using ASCII words.</summary>
    public static GlyphSet Ascii { get; } = new(
        new Dictionary<RuleStatus, string>
        {
            [RuleStatus.Passed] = "ok",
            [RuleStatus.Warning] = "warn",
            [RuleStatus.Failed] = "FAIL",
            [RuleStatus.Skipped] = "skip",
            [RuleStatus.NotApplicable] = "n/a",
            [RuleStatus.Errored] = "ERROR",
        },
        separator: "-",
        arrow: "->",
        absent: "-",
        isUnicode: false);

    /// <summary>Whether this set uses the unicode glyphs.</summary>
    public bool IsUnicode { get; }

    /// <summary>
    /// The widest glyph in this set, for aligning the column.
    /// </summary>
    public int Width => _glyphs.Values.Max(glyph => glyph.Length);

    /// <summary>
    /// Chooses the variant this run will print.
    /// </summary>
    /// <param name="capabilities">The console being written to.</param>
    /// <param name="noUnicodeRequested"><c>--no-unicode</c> was passed.</param>
    /// <remarks>
    /// The flag is an instruction, not a preference: asked for ASCII, the
    /// answer is ASCII even on a console that could render the alternative. The
    /// automatic fallback only decides the case where the user said nothing.
    /// </remarks>
    public static GlyphSet Select(ConsoleCapabilities capabilities, bool noUnicodeRequested)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        if (noUnicodeRequested)
        {
            return Ascii;
        }

        return capabilities.CanRender(Unicode._glyphs.Values) ? Unicode : Ascii;
    }

    /// <summary>
    /// The glyph for <paramref name="status"/>.
    /// </summary>
    public string For(RuleStatus status) =>
        _glyphs.TryGetValue(status, out var glyph)
            ? glyph
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped rule status.");
}
