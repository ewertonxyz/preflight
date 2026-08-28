namespace Preflight.Cli.Tests;

using System.Text;

/// <summary>
/// Fixes the one write this tool performs inside a workspace.
/// </summary>
/// <remarks>
/// Against a real temporary directory rather than a substitute, for the reason
/// <c>FileRuleCacheStore</c> is: this type exists to touch the disk, and a test
/// that replaced the disk would assert nothing about the only thing it does.
/// </remarks>
public sealed class WorkspaceFileWriterTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("preflight-writer-");
    private readonly WorkspaceFileWriter _writer = new();

    public void Dispose() => _directory.Delete(recursive: true);

    private string PathTo(string name) => Path.Combine(_directory.FullName, name);

    [Fact]
    public async Task WriteNewAsync_WhenNothingIsThere_WritesTheContent()
    {
        var path = PathTo("manifest.json");

        await _writer.WriteNewAsync(path, "{}", TestContext.Current.CancellationToken);

        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ShouldBe("{}");
    }

    /// <summary>
    /// The write refuses to replace, and leaves no staging file behind.
    /// </summary>
    /// <remarks>
    /// The refusal lives here rather than in the caller's existence check,
    /// which is the point: between that check and this write another process
    /// can create the file, and only <see cref="File.Move(string, string)"/>
    /// without <c>overwrite</c> notices. The directory listing is the second
    /// half — a staging file left behind is litter somebody finds a year later
    /// and cannot attribute to anything.
    /// </remarks>
    [Fact]
    public async Task WriteNewAsync_WhenTheTargetExists_ThrowsAndLeavesBothTheFileAndTheDirectoryAlone()
    {
        var path = PathTo("manifest.json");
        await File.WriteAllTextAsync(path, "original", Encoding.UTF8, TestContext.Current.CancellationToken);

        await Should.ThrowAsync<IOException>(
            () => _writer.WriteNewAsync(path, "replacement", TestContext.Current.CancellationToken));

        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ShouldBe("original");
        _directory.GetFiles().Select(file => file.Name).ShouldBe(["manifest.json"]);
    }

    [Fact]
    public void Exists_ForAFile_IsTrue()
    {
        var path = PathTo("manifest.json");
        File.WriteAllText(path, "{}");

        _writer.Exists(path).ShouldBeTrue();
    }

    /// <remarks>
    /// A directory occupying the name is still a reason to stop: the write
    /// would fail, and the message a user deserves says the path is taken
    /// rather than reporting whatever the file system raised.
    /// </remarks>
    [Fact]
    public void Exists_ForADirectoryWithTheSameName_IsTrue()
    {
        var path = PathTo("manifest.json");
        Directory.CreateDirectory(path);

        _writer.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public void Exists_ForNothing_IsFalse() =>
        _writer.Exists(PathTo("absent.json")).ShouldBeFalse();
}
