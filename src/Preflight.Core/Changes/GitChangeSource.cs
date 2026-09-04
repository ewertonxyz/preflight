namespace Preflight.Core.Changes;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Services;

/// <summary>
/// Produces the changed-file list by asking git.
/// </summary>
/// <remarks>
/// <para>
/// Lives in <c>Preflight.Core</c>, and takes an <see cref="IProcessRunner"/>
/// rather than starting a process itself. The seam exists so the expensive,
/// environment-bound work stays testable; a <c>Process.Start</c> inline here
/// would make the parser reachable only through a real repository.
/// </para>
/// <para>
/// The engine never fetches. Downloading an artefact is a declared non-goal,
/// and the tempting place to break it is exactly here: a shallow CI clone where
/// <c>origin/main</c> does not resolve invites a <c>git fetch</c> to make the
/// problem go away. Every command issued is a read.
/// </para>
/// </remarks>
public sealed class GitChangeSource : IChangeSource
{
    private readonly IProcessRunner _processes;

    public GitChangeSource(IProcessRunner processes)
    {
        _processes = processes;
    }

    public string Name => "git";

    public async Task<IReadOnlyList<ChangedFile>> GetChangesAsync(
        DirectoryInfo workspaceRoot,
        string? fromRef,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);

        if (string.IsNullOrWhiteSpace(fromRef))
        {
            // The CLI refuses this before it gets here. The guard
            // stays because this type is public and the engine is hostable
            // without the CLI: an empty list returned silently would make every
            // pre-submit rule report NotApplicable and the run go green having
            // examined nothing.
            throw new ChangeSourceException(
                "The git change source needs a ref to diff against. Pass --changed-from <ref>.");
        }

        var result = await _processes.RunAsync(
            new ProcessRequest
            {
                FileName = "git",

                // -z is not a detail. Without it git quotes any path outside
                // printable ASCII into octal escapes, separates fields with a
                // tab and records with a newline — all three of which are legal
                // inside a filename. With it, records are NUL-separated and
                // never quoted, and three families of parsing branch stop
                // existing rather than being tested.
                //
                // Passed as an argument list, never a concatenated string, so a
                // ref containing a space or a quote cannot become another
                // argument.
                Arguments = ["diff", "--name-status", "-z", fromRef],
                WorkingDirectory = workspaceRoot.FullName,
            },
            cancellationToken);

        return result.ExitCode == 0
            ? Parse(result.StandardOutput)
            : throw new ChangeSourceException(
                $"git could not diff against '{fromRef}': {result.StandardError.Trim()}");
    }

    /// <summary>
    /// Parses the NUL-separated output of <c>git diff --name-status -z</c>.
    /// </summary>
    /// <remarks>
    /// The shape is a status field followed by one path, except for a rename or
    /// a copy, whose status carries a similarity score and is followed by two.
    /// Records are read by consuming fields, not by splitting into pairs.
    /// </remarks>
    private static List<ChangedFile> Parse(string output)
    {
        var fields = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var changes = new List<ChangedFile>();
        var index = 0;

        while (index < fields.Length)
        {
            var status = fields[index++];

            if (index >= fields.Length)
            {
                break;
            }

            var first = Normalise(fields[index++]);

            if (status.Length > 0 && (status[0] == 'R' || status[0] == 'C'))
            {
                if (index >= fields.Length)
                {
                    break;
                }

                var second = Normalise(fields[index++]);

                // A copy is reported as a new file, because that is what it is
                // to every rule that examines it: a path that did not exist
                // before and does now. Only a rename carries the old path.
                changes.Add(status[0] == 'R'
                    ? new ChangedFile(second, ChangeKind.Renamed, first)
                    : new ChangedFile(second, ChangeKind.Added));

                continue;
            }

            if (Classify(status) is { } kind)
            {
                changes.Add(new ChangedFile(first, kind));
            }
        }

        return changes;
    }

    /// <summary>
    /// Maps a git status letter to a <see cref="ChangeKind"/>.
    /// </summary>
    /// <remarks>
    /// git emits nine letters and <see cref="ChangeKind"/> models four. The
    /// three that are silently dropped are deliberate and each has a reason:
    /// <c>T</c> is a type change, where the content the rules examine is
    /// unchanged; <c>U</c> is an unmerged path, which belongs to a conflicted
    /// working tree that a validation run has nothing useful to say about; and
    /// <c>B</c> is a pairing break, which git only emits under a flag this
    /// command does not pass.
    ///
    /// Dropping the rest silently would be the real hazard — a file missing
    /// from the changed set makes <c>large-file</c> report <c>n/a</c> on a
    /// commit that had something to check — so anything unrecognised is an
    /// error, not a skip.
    /// </remarks>
    private static ChangeKind? Classify(string status) => status switch
    {
        "A" => ChangeKind.Added,
        "M" => ChangeKind.Modified,
        "D" => ChangeKind.Deleted,
        "T" or "U" or "B" => null,
        _ => throw new ChangeSourceException(
            $"git reported an unrecognised change status '{status}'."),
    };

    /// <summary>
    /// Puts a path into the one form the rest of the tool uses.
    /// </summary>
    /// <remarks>
    /// git always emits forward slashes; Windows paths use backslashes. A
    /// changed path reaches <c>FindingLocation.RelativePath</c>, the console
    /// report, the JSON, and every glob a rule matches against — so a separator
    /// that varies by operating system makes a golden file fail on one of them
    /// and a pattern match fail on the other.
    /// </remarks>
    private static string Normalise(string path) => path.Replace('\\', '/');
}
