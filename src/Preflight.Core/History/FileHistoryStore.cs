namespace Preflight.Core.History;

using System.Text;

/// <summary>
/// Appends to a real file, with the opening flags an append-only log requires.
/// </summary>
/// <remarks>
/// <para>
/// The assumptions are declared precisely, rather than presuming it: a single
/// <c>WriteFile</c> on a handle opened for append is serialised by Windows and
/// atomic in practice on local Linux file systems, and has
/// <b>no guarantee at all</b> on SMB or NFS. The escape for that last row is
/// <c>historyMode: per-process</c>, which is policy and therefore not this
/// class's decision.
/// </para>
/// <para>
/// What this class owes the assumption is the two mechanics it rests on: the
/// handle is opened for append with sharing, and every record leaves as exactly
/// one write. Both are visible in <see cref="AppendOptions"/> and in
/// <see cref="AppendAsync"/> — deliberately, because a test cannot win a race
/// against the operating system to prove the rest, and a test that has to win a
/// race to pass is one that fails on a loaded machine.
/// </para>
/// </remarks>
public sealed class FileHistoryStore : IHistoryStore
{
    /// <summary>
    /// How the file is opened.
    /// </summary>
    /// <remarks>
    /// <see cref="FileShare.ReadWrite"/> so that a second process appending at
    /// the same time is not refused the handle, and so <c>preflight report</c>
    /// can read the file while a run is writing to it.
    /// </remarks>
    public static FileStreamOptions AppendOptions { get; } = new()
    {
        Mode = FileMode.Append,
        Access = FileAccess.Write,
        Share = FileShare.ReadWrite,
        Options = FileOptions.Asynchronous,
    };

    /// <inheritdoc />
    public async Task AppendAsync(string filePath, string line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(line);

        // Unconditional, because CreateDirectory is idempotent and already
        // handles two processes racing to create the same directory. Guarding it
        // with an existence check would add a branch that says nothing and a
        // window between the two calls that says something wrong.
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        // The terminator is part of the same buffer, so the record and its
        // newline cannot be split into two writes by anything above the
        // operating system. Nothing above that is promised here.
        var bytes = Encoding.UTF8.GetBytes(line + '\n');

        await using var stream = new FileStream(filePath, AppendOptions);

        await stream.WriteAsync(bytes, cancellationToken);
    }
}
