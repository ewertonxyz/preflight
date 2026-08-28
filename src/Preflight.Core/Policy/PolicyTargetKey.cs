namespace Preflight.Core.Policy;

using Preflight.Abstractions;

/// <summary>
/// The target of a run, separating what the user said from what defaulted.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BuildTarget"/> is still the effective target and is not
/// duplicated here. What it cannot answer is the question this layer depends
/// on: <c>--platform</c> falls back to <c>any</c> and <c>--configuration</c> to
/// <c>Development</c>, so a <c>targets</c> block matching those defaults would
/// apply one platform's thresholds to every run that forgot the flag.
/// </para>
/// <para>
/// ADR-015 says refuse rather than assume, and a default that predates the
/// feature and quietly starts selecting policy is assuming. See ADR-030.
/// </para>
/// </remarks>
/// <param name="Effective">The target the rules receive.</param>
/// <param name="PlatformStated">Whether <c>--platform</c> was given.</param>
/// <param name="ConfigurationStated">Whether <c>--configuration</c> was given.</param>
public sealed record StatedBuildTarget(
    BuildTarget Effective,
    bool PlatformStated,
    bool ConfigurationStated)
{
    /// <summary>A run that stated neither axis.</summary>
    /// <remarks>
    /// The default for every caller that does not care — a policy read for a
    /// command that names no target still has to build one, and this says
    /// plainly that no <c>targets</c> block can match it.
    /// </remarks>
    public static StatedBuildTarget Unstated { get; } =
        new(new BuildTarget("any", "Development"), PlatformStated: false, ConfigurationStated: false);
}

/// <summary>
/// The key of one entry in a policy's <c>targets</c> block.
/// </summary>
/// <remarks>
/// <c>plataforma</c> or <c>plataforma|configuração</c>, and nothing else. There
/// is no glob, for the reason <c>compileProbe.inputs</c> has none and version
/// ranges have no grammar (ADR-023): a pattern language is a parser to write
/// and test before two strings can be compared. See <c>Docs/design.md 6.2</c>
/// and ADR-030.
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
    /// That is the whole of the rule: <c>--configuration</c> falls back to
    /// <c>Development</c>, so a <c>win64|Development</c> block would otherwise
    /// fire on a run that said only <c>--platform win64</c>, handing somebody
    /// one configuration's thresholds because they omitted a flag — and calling
    /// it a pass.
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
