namespace Preflight.Cli.Tests;

using System.Text;

/// <summary>
/// Writes a file into a test's temporary workspace, creating the directories
/// above it.
/// </summary>
/// <remarks>
/// <para>
/// Seven test classes had grown their own copy of this, and the copies had
/// already stopped agreeing on two things. Three normalised the separator in
/// the relative path and four did not, so <c>"rules/acme.json"</c> named one
/// file in one class and another in the next. And four wrote a byte order mark
/// while three did not, so whether the policy loader was being handed the bytes
/// a real workspace holds depended on which class you were reading. Nothing
/// failed over either, which is what makes a duplicated fixture expensive: the
/// divergence is invisible until the day one arm of it is the reason a test
/// passes.
/// </para>
/// <para>
/// The byte order mark stays, because <c>WorkspaceFileWriter</c> and
/// <c>MachineStateStore</c> both write one — a fixture that omitted it would be
/// handing the readers bytes this tool never produces.
/// </para>
/// </remarks>
public static class WorkspaceFiles
{
    /// <summary>Writes <paramref name="content"/> under <paramref name="directory"/>.</summary>
    /// <param name="directory">The workspace the path is relative to.</param>
    /// <param name="relativePath">
    /// A path below it, written with <c>/</c> whatever the platform separator is.
    /// </param>
    /// <param name="content">The bytes to write.</param>
    public static void Write(DirectoryInfo directory, string relativePath, string content)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(relativePath);

        var path = Path.Combine(
            directory.FullName,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
    }
}
