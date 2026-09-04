namespace Preflight.Cli.Tests;

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using static Preflight.TestSupport.RepositoryLayout;

/// <summary>
/// Guards what this repository publishes: no file it tracks names an assistant,
/// and none cites a document its readers do not have.
/// </summary>
/// <remarks>
/// <para>
/// Both rules were true before this test existed, and that is exactly why it
/// exists. They were made true by three reviews reading files one at a time,
/// and nothing stopped the fourth edit from putting a citation back. A rule
/// that only a person can check is a rule that holds until the day somebody is
/// in a hurry.
/// </para>
/// <para>
/// The set of files is whatever <c>git ls-files</c> reports, and that is the
/// definition rather than a convenience. "Published" means tracked: a path this
/// repository deliberately leaves untracked is free to say whatever it likes,
/// and asking git removes any need for this test to carry a list of those paths
/// — a list which would itself have to name them, in a tracked file, which is
/// the thing being forbidden.
/// </para>
/// <para>
/// What is checked is narrow on purpose. Every pattern below is one no
/// legitimate line in this repository can match, so a failure is always a real
/// one. The wider half of the rule — vocabulary that reads as nobody's voice, a
/// comment that points at a decision instead of stating it, prose in the wrong
/// language — cannot be expressed as a pattern without failing on lines that
/// are fine, and a guard that cries wolf is a guard somebody deletes. That half
/// stays with the reviewer; this half can never regress unnoticed again.
/// </para>
/// <para>
/// It lives in the command line project's tests because the repository has no
/// test project of its own and this is where the other repository-wide guards
/// already are. A project for one file would be worse than the mild awkwardness
/// of it sitting here.
/// </para>
/// </remarks>
public sealed class PublishedTreeTests
{
    /// <summary>
    /// Every tracked file, as text, read once for the whole class.
    /// </summary>
    /// <remarks>
    /// Lazy and static because both tests want the same few hundred files, and
    /// reading them twice would double the cost of the cheapest guard in the
    /// suite for nothing.
    /// </remarks>
    private static readonly Lazy<IReadOnlyList<(string Path, string[] Lines)>> TrackedFiles =
        new(ReadTrackedFiles);

    /// <summary>
    /// Nothing in the published tree refers to an assistant.
    /// </summary>
    /// <remarks>
    /// The repository reads as a codebase rather than as a transcript of how it
    /// came to exist. A trailer or a mention would tell a reader something
    /// about the process and nothing about the code, and it would be there for
    /// as long as the history is.
    /// </remarks>
    [Theory]
    [InlineData(@"claude")]
    [InlineData(@"anthropic")]
    [InlineData(@"co-authored-by")]
    [InlineData(@"generated with")]
    public void TrackedFiles_NameNoAssistant(string pattern) =>
        Occurrences(pattern, RegexOptions.IgnoreCase).ShouldBeEmpty(
            "the published tree names no assistant, in any file, ever.");

    /// <summary>
    /// Nothing in the published tree cites something its reader cannot open.
    /// </summary>
    /// <remarks>
    /// Somebody who clones this repository has none of the documents these
    /// patterns point at. To them a decision-record number says "the reason
    /// exists somewhere you cannot reach", which is worse than no comment at
    /// all: it reads as an appeal to authority standing where the argument
    /// should be. A comment states the decision, with the alternative it
    /// rejected beside it.
    /// </remarks>
    [Theory]
    [InlineData(@"\bADR\b")]
    [InlineData(@"IDEAS\.md")]
    [InlineData(@"CLAUDE\.md")]
    [InlineData(@"\.claude/")]
    [InlineData(@"§")]
    [InlineData(@"[Ss]ection \d+\.\d")]
    [InlineData(@"principle \d")]
    [InlineData(@"the (design document|glossary|plan says|plan lists)")]
    public void TrackedFiles_CiteNothingAReaderCannotOpen(string pattern) =>
        Occurrences(pattern, RegexOptions.IgnoreCase).ShouldBeEmpty(
            "state the decision in the file; do not point at where it is written.");

    /// <summary>
    /// The private documentation folder, matched case-sensitively.
    /// </summary>
    /// <remarks>
    /// Its own test because the case matters here and nowhere else. A
    /// lower-case <c>docs/</c> is an ordinary segment of somebody else's URL,
    /// and matching it would fail this suite over a link to a library's manual.
    /// </remarks>
    [Fact]
    public void TrackedFiles_DoNotCiteThePrivateDocumentationFolder() =>
        Occurrences("Docs/", RegexOptions.None).ShouldBeEmpty(
            "state the decision in the file; do not point at the folder it is written in.");

    /// <summary>
    /// The one file the scan skips: this one.
    /// </summary>
    /// <remarks>
    /// A guard that lists what it forbids has to write those words down, and
    /// this file is the only place in the tree where they are data rather than
    /// prose. Skipping it by name is the whole exemption — there is no list to
    /// grow, and anything added here to dodge a failure would be visible in the
    /// same diff that added it.
    /// </remarks>
    private const string ThisFile = nameof(PublishedTreeTests) + ".cs";

    /// <remarks>
    /// Matched against the file flattened to one line, not against each line in
    /// turn, and that is the difference between a guard and a guard with a hole
    /// in it. Comments here wrap at about eighty characters, so a citation lands
    /// across a line break as often as not — <c>Section</c> ending one line and
    /// <c>11.3</c> starting the next. A line-at-a-time scan sees neither half
    /// and reports the file as clean, which is the worst thing a check like this
    /// can do. The original line is recovered from the offset of the match, so a
    /// failure still names the line somebody has to open.
    /// </remarks>
    private static IReadOnlyList<string> Occurrences(string pattern, RegexOptions options)
    {
        var expression = new Regex(pattern, options, TimeSpan.FromSeconds(5));

        return
        [
            .. from file in TrackedFiles.Value
               where !file.Path.EndsWith(ThisFile, StringComparison.Ordinal)
               let flattened = Flatten(file.Lines)
               from match in expression.Matches(flattened.Text).Cast<Match>()
               let line = LineAt(flattened.LineStarts, match.Index)
               select $"{file.Path}:{line}: {Excerpt(file.Lines[line - 1])}",
        ];
    }

    /// <summary>
    /// The file as a single line, with the offset each original line starts at.
    /// </summary>
    /// <remarks>
    /// Each line contributes its text with any leading comment marker removed,
    /// joined by one space. Dropping the marker is what closes the wrap: without
    /// it a citation split across two documentation lines is still separated by
    /// <c>///</c>, and the pattern that was supposed to find it does not.
    /// </remarks>
    private static (string Text, int[] LineStarts) Flatten(string[] lines)
    {
        var builder = new StringBuilder();
        var starts = new int[lines.Length];

        for (var index = 0; index < lines.Length; index++)
        {
            starts[index] = builder.Length;
            builder.Append(CommentMarker.Replace(lines[index], string.Empty)).Append(' ');
        }

        return (builder.ToString(), starts);
    }

    private static readonly Regex CommentMarker = new(@"^\s*(///|//|#|\*|<!--)?\s*", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static int LineAt(int[] lineStarts, int offset)
    {
        var found = Array.BinarySearch(lineStarts, offset);

        // BinarySearch returns the complement of the next-larger index when the
        // offset falls inside a line rather than on its first character, which
        // is the ordinary case: the line wanted is the one before it.
        return found >= 0 ? found + 1 : ~found;
    }

    /// <remarks>
    /// Trimmed and capped so that a failure over a minified or generated line
    /// prints something a person can read rather than a screenful.
    /// </remarks>
    private static string Excerpt(string line)
    {
        var trimmed = line.Trim();

        return trimmed.Length <= 120 ? trimmed : string.Concat(trimmed.AsSpan(0, 117), "...");
    }

    /// <remarks>
    /// <c>git ls-files</c> and not a directory walk. A walk would have to be
    /// told about build output, the artifacts folder and every path this
    /// repository keeps out of the tree, and that list would rot; git already
    /// holds the only answer that is correct by definition. A checkout is what
    /// this repository always is, so a missing git is a broken environment
    /// rather than a case to tolerate quietly — the exception says so.
    /// </remarks>
    private static IReadOnlyList<(string Path, string[] Lines)> ReadTrackedFiles()
    {
        var root = RepositoryRoot();

        using var process = Process.Start(new ProcessStartInfo("git")
        {
            ArgumentList = { "ls-files" },
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("git did not start.");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'git ls-files' failed in '{root}' with exit code {process.ExitCode}. " +
                "This test reads the tracked file list, so it needs a git checkout.");
        }

        return
        [
            .. from relative in output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
               let path = Path.Combine(root, relative.Trim())
               where File.Exists(path)
               select (relative.Trim(), File.ReadAllLines(path)),
        ];
    }
}
