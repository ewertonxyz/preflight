namespace Preflight.Core.Tests.Execution;

using System.Text;
using Preflight.Abstractions;
using Preflight.Core;

/// <summary>
/// Fixes the one <see cref="IFileSystem"/> that ships.
/// </summary>
/// <remarks>
/// Every rule test substitutes this interface, which is the point of it
/// existing — and the consequence is that nothing else would ever run the real
/// implementation. A member wired to the wrong BCL call, or one that silently
/// returned a default, would leave the whole suite green and fail on the first
/// real workspace.
/// </remarks>
public sealed class PhysicalFileSystemTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("preflight-fs-");
    private readonly PhysicalFileSystem _fileSystem = new();

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void FileExists_DistinguishesAFileFromNothing()
    {
        var path = Write("present.txt", "x");

        _fileSystem.FileExists(path).ShouldBeTrue();
        _fileSystem.FileExists(Path.Combine(_root.FullName, "absent.txt")).ShouldBeFalse();
    }

    /// <remarks>
    /// A directory is not a file. <see cref="File.Exists"/> agrees, and the test
    /// is here because the naive implementation people reach for —
    /// <c>Path.Exists</c> — does not.
    /// </remarks>
    [Fact]
    public void FileExists_ForADirectory_IsFalse()
    {
        _fileSystem.FileExists(_root.FullName).ShouldBeFalse();
    }

    [Fact]
    public void DirectoryExists_DistinguishesADirectoryFromAFile()
    {
        var path = Write("present.txt", "x");

        _fileSystem.DirectoryExists(_root.FullName).ShouldBeTrue();
        _fileSystem.DirectoryExists(path).ShouldBeFalse();
    }

    /// <remarks>
    /// Bytes, not characters. <c>core.presubmit.large-file</c> compares this
    /// against a <c>maxBytes</c> from policy, and a length in characters would
    /// under-report every non-ASCII file — quietly letting an oversized asset
    /// through, which is the direction that matters.
    /// </remarks>
    [Fact]
    public void GetFileSize_ReportsBytesRatherThanCharacters()
    {
        var path = Write("size.txt", "café");

        _fileSystem.GetFileSize(path).ShouldBe(Encoding.UTF8.GetByteCount("café"));
    }

    [Fact]
    public async Task ReadAllTextAsync_ReturnsTheContent()
    {
        var path = Write("text.txt", "hello");

        (await _fileSystem.ReadAllTextAsync(path, CancellationToken.None)).ShouldBe("hello");
    }

    [Fact]
    public async Task ReadAllBytesAsync_ReturnsTheBytes()
    {
        var path = Write("bytes.txt", "hi");

        (await _fileSystem.ReadAllBytesAsync(path, CancellationToken.None))
            .ShouldBe(Encoding.UTF8.GetBytes("hi"));
    }

    [Fact]
    public void OpenRead_ReadsTheContent()
    {
        var path = Write("stream.txt", "streamed");

        using var stream = _fileSystem.OpenRead(path);
        using var reader = new StreamReader(stream);

        reader.ReadToEnd().ShouldBe("streamed");
    }

    /// <remarks>
    /// Both search options, because a rule that walks a workspace tree and one
    /// that inspects a single directory are different jobs, and a member wired
    /// to ignore the argument would satisfy either test alone.
    /// </remarks>
    [Fact]
    public void EnumerateFiles_HonoursThePatternAndTheSearchOption()
    {
        Write("a.json", "{}");
        Write("b.txt", "x");
        Directory.CreateDirectory(Path.Combine(_root.FullName, "nested"));
        Write(Path.Combine("nested", "c.json"), "{}");

        var shallow = _fileSystem
            .EnumerateFiles(_root.FullName, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .ToArray();

        var deep = _fileSystem
            .EnumerateFiles(_root.FullName, "*.json", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        shallow.ShouldBe(["a.json"]);
        deep.ShouldBe(["a.json", "c.json"]);
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_root.FullName, relativePath);

        File.WriteAllText(path, content);

        return path;
    }
}
