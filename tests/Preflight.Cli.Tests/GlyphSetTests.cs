namespace Preflight.Cli.Tests;

using System.Text;
using Preflight.Abstractions;

/// <summary>
/// Fixes the glyph table of the console report and the choice
/// between its two variants.
/// </summary>
/// <remarks>
/// Every expectation here is written as an escape rather than as the character
/// itself. The bytes a literal compiles to depend on the encoding of this
/// source file, so a test written with the character asserts whatever the file
/// happens to be saved as — which is not an assertion about the tool.
/// </remarks>
public sealed class GlyphSetTests
{
    private static ConsoleCapabilities ConsoleWith(Encoding encoding) =>
        new(TextWriter.Null, encoding, IsInteractive: false, IsInputInteractive: false, ConsoleCapabilities.DefaultWidth);

    [Theory]
    [InlineData(RuleStatus.Passed, "\u2713")]
    [InlineData(RuleStatus.Warning, "!")]
    [InlineData(RuleStatus.Failed, "\u2717")]
    [InlineData(RuleStatus.Skipped, "\u2298")]
    [InlineData(RuleStatus.NotApplicable, "\u00b7")]
    [InlineData(RuleStatus.Errored, "\u2a02")]
    public void Unicode_MatchesTheDocumentedTable(RuleStatus status, string expected)
    {
        GlyphSet.Unicode.For(status).ShouldBe(expected);
    }

    [Theory]
    [InlineData(RuleStatus.Passed, "ok")]
    [InlineData(RuleStatus.Warning, "warn")]
    [InlineData(RuleStatus.Failed, "FAIL")]
    [InlineData(RuleStatus.Skipped, "skip")]
    [InlineData(RuleStatus.NotApplicable, "n/a")]
    [InlineData(RuleStatus.Errored, "ERROR")]
    public void Ascii_MatchesTheDocumentedTable(RuleStatus status, string expected)
    {
        GlyphSet.Ascii.For(status).ShouldBe(expected);
    }

    /// <remarks>
    /// This branch is not hypothetical: several Windows build
    /// agents default to an encoding that cannot carry these glyphs, and the
    /// encoding does not fail when asked to — it substitutes, leaving a report
    /// that is a row of question marks.
    /// </remarks>
    [Fact]
    public void Select_WhenTheEncodingCannotCarryTheGlyphs_FallsBackToAscii()
    {
        GlyphSet.Select(ConsoleWith(Encoding.ASCII), noUnicodeRequested: false)
            .IsUnicode.ShouldBeFalse();
    }

    [Fact]
    public void Select_WhenTheEncodingCarriesOnlyLatin1_FallsBackToAscii()
    {
        // Latin1 carries U+00B7 and none of the others, so it also proves the
        // check is not satisfied by a single representable glyph.
        GlyphSet.Select(ConsoleWith(Encoding.Latin1), noUnicodeRequested: false)
            .IsUnicode.ShouldBeFalse();
    }

    /// <remarks>
    /// Without this, an implementation that always returned ASCII would satisfy
    /// both fallback tests. It asserts the <em>choice</em>, which the two above
    /// cannot.
    /// </remarks>
    [Fact]
    public void Select_OnAUtf8ConsoleWithNoFlag_UsesUnicode()
    {
        GlyphSet.Select(ConsoleWith(Encoding.UTF8), noUnicodeRequested: false)
            .IsUnicode.ShouldBeTrue();
    }

    /// <remarks>
    /// The flag is an instruction, not a preference. A console that could render
    /// the alternative does not get to overrule the person who asked for ASCII.
    /// </remarks>
    [Fact]
    public void Select_WithNoUnicodeRequested_UsesAsciiEvenOnAUtf8Console()
    {
        GlyphSet.Select(ConsoleWith(Encoding.UTF8), noUnicodeRequested: true)
            .IsUnicode.ShouldBeFalse();
    }

    [Fact]
    public void For_WithAStatusOutsideTheEnum_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => GlyphSet.Unicode.For((RuleStatus)99));
    }

    /// <remarks>
    /// The width is what the report aligns on, and the two variants disagree:
    /// the widest ASCII word is five characters, the widest unicode glyph is
    /// one. A single hard-coded column would misalign one of them.
    /// </remarks>
    [Fact]
    public void Width_IsTheWidestGlyphInTheSet()
    {
        GlyphSet.Unicode.Width.ShouldBe(1);
        GlyphSet.Ascii.Width.ShouldBe(5);
    }
}

/// <summary>
/// Fixes the console facts the reporter branches on.
/// </summary>
public sealed class ConsoleCapabilitiesTests
{
    private static readonly string[] UnicodeGlyphs = ["\u2713", "\u2717", "\u2298", "\u00b7", "\u2a02"];

    private static ConsoleCapabilities With(Encoding encoding) =>
        new(TextWriter.Null, encoding, IsInteractive: false, IsInputInteractive: false, ConsoleCapabilities.DefaultWidth);

    [Fact]
    public void CanRender_WithUtf8_IsTrueForEveryGlyph()
    {
        With(Encoding.UTF8).CanRender(UnicodeGlyphs).ShouldBeTrue();
    }

    /// <remarks>
    /// The round trip is the whole method. ASCII does not refuse U+2713 — it
    /// substitutes a question mark and reports success, which is why asking the
    /// encoding to carry the text and comparing what comes back is the only
    /// check that catches every codepage without listing them.
    /// </remarks>
    [Fact]
    public void CanRender_WithAscii_IsFalse()
    {
        With(Encoding.ASCII).CanRender(UnicodeGlyphs).ShouldBeFalse();
    }

    [Fact]
    public void CanRender_WithAnEmptySet_IsTrue()
    {
        With(Encoding.ASCII).CanRender([]).ShouldBeTrue();
    }

    /// <remarks>
    /// <see cref="Console.WindowWidth"/> throws when no console is attached,
    /// which is every CI agent and every coverage run — including this one. The
    /// assertion is therefore that detection <em>survives</em>, not that it
    /// returns any particular number.
    /// </remarks>
    [Fact]
    public void Detect_UnderATestHost_DoesNotThrowAndReportsAUsableWidth()
    {
        var capabilities = ConsoleCapabilities.Detect();

        capabilities.Width.ShouldBeGreaterThan(0);
        capabilities.Encoding.ShouldNotBeNull();
        capabilities.Output.ShouldNotBeNull();
    }
}
