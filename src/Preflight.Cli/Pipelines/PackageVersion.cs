namespace Preflight.Cli.Pipelines;

using System.Globalization;

/// <summary>
/// The version of an installed pipeline package. Three ordered numeric parts.
/// </summary>
/// <remarks>
/// <para>
/// Its own type rather than <see cref="Version"/>, and the difference is not
/// cosmetic. That one carries four components and uses <c>-1</c> for the ones
/// nobody wrote, so <c>1.4</c> and <c>1.4.0</c> compare differently and
/// <c>Build</c> and <c>Revision</c> become an axis this project never declared.
/// The comparison needed here is the one the workspace manifest already
/// documents — a lower bound that includes and an upper bound that excludes —
/// and it fits in three integers.
/// </para>
/// <para>
/// Not SemVer either: pre-release ordering is a specification to implement and
/// test for a case a studio delivery channel does not have, because a package is
/// published or it is not. <c>1.4.0-rc1</c> and <c>1.4.0+build2</c> fail to
/// parse rather than sorting somewhere surprising. It is the same refusal this
/// codebase applies to glob patterns and string ranges: a grammar nobody asked
/// for is a grammar to document, to test and to explain in a refusal.
/// </para>
/// <para>
/// The rule that a patch difference never decides compatibility governs
/// <c>Preflight.Abstractions</c>, and nothing else: it says whether a package's
/// assemblies load in this binary at all. Here <c>1.4.1</c> and <c>1.4.0</c>
/// are two different versions. The two axes look alike and never meet;
/// confusing them produces a package that installs and does not load, or a
/// binary that refuses one that is perfectly compatible.
/// </para>
/// </remarks>
/// <param name="Major">The first component.</param>
/// <param name="Minor">The second component.</param>
/// <param name="Patch">The third component.</param>
public sealed record PackageVersion(int Major, int Minor, int Patch)
    : IComparable<PackageVersion>
{
    /// <summary>
    /// Reads the canonical spelling, or refuses it.
    /// </summary>
    /// <remarks>
    /// Exactly three components, each a non-negative decimal integer. Anything
    /// else — two components, four, a leading <c>v</c>, a sign, a pre-release
    /// suffix, build metadata, or an empty string — is false rather than a
    /// best-effort reading. A version this type accepted loosely would become a
    /// directory name and then the answer to "which policy did this run use".
    /// </remarks>
    /// <param name="text">The candidate spelling.</param>
    /// <param name="version">The parsed version, when this returns true.</param>
    /// <returns>Whether <paramref name="text"/> is a package version.</returns>
    public static bool TryParse(string? text, out PackageVersion? version)
    {
        version = null;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var parts = text.Split('.');

        if (parts.Length != 3)
        {
            return false;
        }

        var numbers = new int[3];

        for (var index = 0; index < 3; index++)
        {
            // NumberStyles.None rather than the default: the default accepts a
            // leading sign and surrounding whitespace, and "-1.4.0" or " 1.4.0"
            // reaching a directory name is the class of thing this method exists
            // to stop.
            if (!int.TryParse(
                parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]))
            {
                return false;
            }
        }

        version = new PackageVersion(numbers[0], numbers[1], numbers[2]);

        return true;
    }

    /// <summary>
    /// Orders numerically, component by component.
    /// </summary>
    /// <remarks>
    /// The reason this is written rather than inherited: ordinal string
    /// comparison — the habit of this codebase, and the right answer for rule
    /// ids and file names — puts <c>1.9.0</c> after <c>1.10.0</c>. "The newest
    /// installed version" decided that way installs the wrong policy in
    /// silence — reporting success over checks the policy never ran, which is
    /// the worst thing a validation tool can do.
    /// </remarks>
    public int CompareTo(PackageVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var major = Major.CompareTo(other.Major);

        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);

        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    /// <summary>
    /// Whether this version falls inside <paramref name="requirement"/>.
    /// </summary>
    /// <remarks>
    /// Minimum inclusive, maximum exclusive, and an absent maximum means no
    /// upper bound. The same convention the workspace manifest states for a
    /// tool's version range, reused rather than reinvented — "anything in 1.x"
    /// is written <c>1.0.0</c> to <c>2.0.0</c> in both places.
    /// </remarks>
    /// <param name="requirement">The range the checkout declared.</param>
    public bool Satisfies(PipelineRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        if (CompareTo(requirement.Minimum) < 0)
        {
            return false;
        }

        return requirement.Maximum is null || CompareTo(requirement.Maximum) < 0;
    }

    /// <summary>Whether <paramref name="left"/> is older than <paramref name="right"/>.</summary>
    /// <remarks>
    /// The four relational operators exist because the analyser requires them of
    /// anything comparable, and they are the readable spelling at the call sites
    /// that ask whether an installed version is below a bound. They delegate to
    /// <see cref="CompareTo"/>, which is where the rule actually lives.
    /// </remarks>
    public static bool operator <(PackageVersion? left, PackageVersion? right) =>
        Compare(left, right) < 0;

    /// <summary>Whether <paramref name="left"/> is older than or equal to <paramref name="right"/>.</summary>
    public static bool operator <=(PackageVersion? left, PackageVersion? right) =>
        Compare(left, right) <= 0;

    /// <summary>Whether <paramref name="left"/> is newer than <paramref name="right"/>.</summary>
    public static bool operator >(PackageVersion? left, PackageVersion? right) =>
        Compare(left, right) > 0;

    /// <summary>Whether <paramref name="left"/> is newer than or equal to <paramref name="right"/>.</summary>
    public static bool operator >=(PackageVersion? left, PackageVersion? right) =>
        Compare(left, right) >= 0;

    private static int Compare(PackageVersion? left, PackageVersion? right) =>
        left is null ? (right is null ? 0 : -1) : left.CompareTo(right);

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
}
