namespace Preflight.Rules;

/// <summary>
/// Finds a path component named after a Windows device.
/// </summary>
/// <remarks>
/// These names are reserved whatever extension follows them, so
/// <c>aux.cs</c> is as unwritable as <c>aux</c>, and they are reserved in every
/// directory rather than only at the root. A repository that contains one
/// cannot be checked out on Windows, and the error names the clone rather than
/// the file.
/// </remarks>
internal sealed class ReservedNameCheck : IPathPortabilityCheck
{
    /// <summary>
    /// The name the policy selects this check by.
    /// </summary>
    public const string CheckName = "reserved-name";

    /// <summary>
    /// The twenty-two device names Windows reserves.
    /// </summary>
    /// <remarks>
    /// Written out rather than generated from a prefix and a digit range,
    /// because the set is closed and a generator would invite the two mistakes
    /// this check exists to avoid: <c>COM0</c>, which is not reserved, and
    /// <c>COM10</c>, which is not either.
    /// </remarks>
    public static readonly string[] Reserved =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <inheritdoc/>
    public string Name => CheckName;

    /// <inheritdoc/>
    public IReadOnlyList<PathDefect> Inspect(IReadOnlyList<string> paths, PathPortabilityLimits limits)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var defects = new List<PathDefect>();

        foreach (var path in paths)
        {
            foreach (var component in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Reserves(component))
                {
                    defects.Add(new PathDefect(
                        path,
                        $"the component '{component}' is a reserved device name",
                        "no path component named after a Windows device"));
                }
            }
        }

        return defects;
    }

    /// <remarks>
    /// The stem, taken up to the first dot rather than the last, because the
    /// reservation covers everything after the name: <c>aux.tar.gz</c> is as
    /// unwritable as <c>aux</c>. Compared whole and without regard to case, so
    /// that <c>com0</c> and <c>com10</c> — which are not reserved — stay
    /// ordinary names.
    /// </remarks>
    private static bool Reserves(string component)
    {
        var stem = component.Split('.', 2)[0];

        return Array.Exists(Reserved, name => string.Equals(name, stem, StringComparison.OrdinalIgnoreCase));
    }
}
