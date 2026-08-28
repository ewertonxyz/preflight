namespace Preflight.Core.Tests.History;

using System.Text;
using Preflight.Abstractions;

/// <summary>
/// The three <see cref="IFileSystem"/> members the history reader uses, over a
/// dictionary.
/// </summary>
/// <remarks>
/// A class rather than a substitute because <c>OpenRead</c> has to hand back a
/// fresh stream on every call, and a substitute expressing that reads worse than
/// the four lines below. The remaining members throw: a reader that reached one
/// of them would be doing something these tests never asked for, and finding
/// that out loudly is the point.
/// </remarks>
public sealed class InMemoryHistoryFiles : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    public bool DirectoryIsThere { get; set; } = true;

    /// <summary>
    /// The order <c>EnumerateFiles</c> hands the files back in.
    /// </summary>
    /// <remarks>
    /// Settable so a test can shuffle it. <c>Directory.EnumerateFiles</c>
    /// promises no order, and the determinism guarantee refuses to let the file system decide
    /// anything a report prints.
    /// </remarks>
    public List<string> EnumerationOrder { get; } = [];

    public void Add(string path, string content)
    {
        _files[path] = content;
        EnumerationOrder.Add(path);
    }

    public bool DirectoryExists(string path) => DirectoryIsThere;

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) =>
        EnumerationOrder;

    public Stream OpenRead(string path) => new MemoryStream(Encoding.UTF8.GetBytes(_files[path]));

    public bool FileExists(string path) => _files.ContainsKey(path);

    public long GetFileSize(string path) => Encoding.UTF8.GetByteCount(_files[path]);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
