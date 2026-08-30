namespace Preflight.Core.Policy;

/// <summary>
/// The key of one entry in a policy's <c>targets</c> block.
/// </summary>
/// <remarks>
/// <c>platform</c> or <c>platform|configuration</c>, and nothing else. There
/// is no glob here, and there is none anywhere else this tool compares two
/// strings: a version range is two explicit bounds, and <c>compileProbe.inputs</c>
/// is a list of paths. A pattern language is a grammar, a parser and a set of
/// edge cases, all of which have to be written and tested before two strings
/// can be compared — and two keys spelled out cost nothing and cannot be read
/// two ways.
/// </remarks>
public readonly record struct PolicyTargetKey(string Platform, string? Configuration)
{
    /// <summary>The word the CLI uses when no platform was given.</summary>
    /// <remarks>
    /// Refused as a target platform. It reads as "any platform" and would mean
    /// the literal string, so a block written with it looks like a wildcard and
    /// behaves like a typo.
    /// </remarks>
    public const string UnstatedPlatform = "any";

    private const char Separator = '|';

    /// <summary>1 for a platform alone, 2 for a platform and a configuration.</summary>
    public int Specificity => Configuration is null ? 1 : 2;

    /// <summary>
    /// Reads a key, or reports that it is not one.
    /// </summary>
    /// <remarks>
    /// Whitespace is not trimmed into validity: a key written with a stray
    /// space is a key that will not match what the user types, and saying so at
    /// load is cheaper than a block that silently never applies.
    /// </remarks>
    public static bool TryParse(string text, out PolicyTargetKey key)
    {
        key = default;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var parts = text.Split(Separator);

        if (parts.Length > 2 || Array.Exists(parts, part => !IsAxisValue(part)))
        {
            return false;
        }

        var platform = parts[0];

        if (string.Equals(platform, UnstatedPlatform, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        key = new PolicyTargetKey(platform, parts.Length == 2 ? parts[1] : null);

        return true;
    }

    /// <remarks>
    /// Letters, digits, <c>-</c> and <c>_</c>, which is the same shape a
    /// pipeline name obeys. <c>*</c> is excluded by that alone, and
    /// deliberately: this is where a glob grammar would start.
    /// </remarks>
    private static bool IsAxisValue(string value) =>
        value.Length > 0 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    /// <summary>
    /// Whether this key applies to <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// An axis the user did not state never matches, whatever it defaulted to.
    /// <c>--configuration</c> falls back to <c>Development</c>, so a
    /// <c>win64|Development</c> block would otherwise fire on a run that said
    /// only <c>--platform win64</c>, handing somebody one configuration's
    /// thresholds because they omitted a flag — and calling it a pass.
    /// </remarks>
    public bool Matches(StatedBuildTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!target.PlatformStated ||
            !string.Equals(Platform, target.Effective.Platform, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Configuration is null ||
            (target.ConfigurationStated &&
                string.Equals(Configuration, target.Effective.Configuration, StringComparison.OrdinalIgnoreCase));
    }
}
