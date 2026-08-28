namespace Preflight.Core.History;

using System.Globalization;
using System.Text;
using Preflight.Core.Policy;

/// <summary>
/// Where one history record is written.
/// </summary>
/// <remarks>
/// Pure, and separated from the writer for that reason: the file name carries a
/// month, a machine and sometimes a process id, and every one of those is a
/// fact a test has to be able to fix. Deciding the name inside the writer would
/// put the assertion behind a file system.
/// </remarks>
public static class HistoryPaths
{
    /// <summary>The extension the history files carry.</summary>
    public const string Extension = ".ndjson";

    /// <summary>The glob a reader uses to find them.</summary>
    public const string SearchPattern = "*" + Extension;

    /// <summary>
    /// The directory the history lives in.
    /// </summary>
    /// <remarks>
    /// A relative <c>historyPath</c> resolves against the workspace root, never
    /// against the current directory. Resolved against the process directory, a
    /// history would split according to where the agent happened to be standing
    /// when it invoked the tool — and the report reads all of it back as one
    /// series.
    /// </remarks>
    public static string DirectoryFor(DirectoryInfo workspaceRoot, HistorySettings settings)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(settings);

        return Path.IsPathRooted(settings.Path)
            ? settings.Path
            : Path.Combine(workspaceRoot.FullName, settings.Path);
    }

    /// <summary>
    /// The file one record written at <paramref name="now"/> belongs to.
    /// </summary>
    /// <remarks>
    /// The month is taken in UTC. In local time two machines in different zones
    /// write different months for the same instant, and <c>--since</c> stops
    /// lining up across the boundary.
    /// </remarks>
    public static string FileNameFor(HistorySettings settings, EngineEnvironment machine, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(machine);

        var month = now.ToUniversalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var host = Sanitise(machine.MachineName);

        return settings.Mode == HistoryMode.PerProcess
            ? $"{month}.{host}.{machine.ProcessId.ToString(CultureInfo.InvariantCulture)}{Extension}"
            : $"{month}.{host}{Extension}";
    }

    /// <summary>
    /// Reduces a machine name to what a file name can carry.
    /// </summary>
    /// <remarks>
    /// A host name is whatever the network told the machine it is called, which
    /// on a domain-joined box can be a fully qualified name with dots in it —
    /// and a dot is the separator this file name is built out of. Everything
    /// outside the ASCII letters, digits, hyphen and underscore becomes a
    /// hyphen, so the name stays both legal and readable.
    /// </remarks>
    private static string Sanitise(string machineName)
    {
        var builder = new StringBuilder(machineName.Length);

        foreach (var character in machineName)
        {
            builder.Append(
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                    ? character
                    : '-');
        }

        return builder.ToString();
    }
}
