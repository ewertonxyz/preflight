namespace Preflight.Core.Tests.Plugins;

using NSubstitute;
using Preflight.Abstractions.Services;
using Preflight.Core.Plugins;

/// <summary>
/// Which directories a run probes, and what it says about the ones it cannot.
/// </summary>
/// <remarks>
/// Everything here goes through a substituted <see cref="IFileSystem"/>, so the
/// table of "given and missing", "given and a file", "implicit and missing" is
/// a set of assertions rather than a set of temporary directories. The pair
/// that carries the design is the two <c>Missing</c> rows: a path the user named
/// is a refusal, and the implicit <c>rules/</c> that is simply not there is
/// nothing at all.
/// </remarks>
public sealed class PluginPathResolutionTests
{
    private static readonly DirectoryInfo Workspace = new(Path.Combine(Path.GetTempPath(), "ws"));
    private static readonly DirectoryInfo Executable = new(Path.Combine(Path.GetTempPath(), "bin"));

    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();

    /// <summary>
    /// A <c>--rules-path</c> that is not there is a refusal naming it.
    /// </summary>
    /// <remarks>
    /// Accepting it and probing nothing would finish a run without the
    /// rules the production declared and report success.
    /// </remarks>
    [Fact]
    public void Resolve_WithAGivenPathThatDoesNotExist_ReportsItAndProbesNothing()
    {
        var result = Resolve("/plugins");

        result.AssemblyPaths.ShouldBeEmpty();
        result.Errors.ShouldHaveSingleItem().Message.ShouldContain("/plugins");
    }

    /// <summary>
    /// The implicit directory being absent is not a problem.
    /// </summary>
    /// <remarks>
    /// The other half of the pair above, and the reason the two cases cannot
    /// share one code path. Every installation with no plugins has no
    /// <c>rules/</c> directory, so treating its absence the way a named path's
    /// absence is treated would make an ordinary deployment invalid.
    /// </remarks>
    [Fact]
    public void Resolve_WithNoGivenPathAndNoImplicitDirectory_IsSilentlyEmpty()
    {
        var result = Resolve();

        result.AssemblyPaths.ShouldBeEmpty();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Resolve_WithAGivenPathThatIsAFile_SaysItMustBeADirectory()
    {
        _fileSystem.FileExists(Rooted("plugins.dll")).Returns(true);

        Resolve(Rooted("plugins.dll"))
            .Errors.ShouldHaveSingleItem()
            .Message.ShouldContain("must be a directory");
    }

    [Fact]
    public void Resolve_WithAnEmptyDirectory_ProbesNothingAndReportsNothing()
    {
        GivenDirectory(Rooted("plugins"));

        var result = Resolve(Rooted("plugins"));

        result.AssemblyPaths.ShouldBeEmpty();
        result.Errors.ShouldBeEmpty();
    }

    /// <remarks>
    /// Sorted ordinally rather than left in enumeration order. Plugin loading
    /// refuses to let load order decide anything, and the order a file system
    /// hands back its entries is exactly what it does not promise.
    /// </remarks>
    [Fact]
    public void Resolve_WithADirectoryOfAssemblies_ReturnsThemInOrdinalOrder()
    {
        GivenDirectory(Rooted("plugins"), "b.dll", "a.dll");

        Resolve(Rooted("plugins")).AssemblyPaths
            .ShouldBe([Rooted("plugins", "a.dll"), Rooted("plugins", "b.dll")]);
    }

    /// <summary>
    /// Only the top level, and only assemblies.
    /// </summary>
    /// <remarks>
    /// A recursive probe turns <c>--rules-path.</c> into an attempt to load
    /// every assembly under a checkout — every <c>bin/</c>, every test binary,
    /// every restored package — and the first one that fails takes the run with
    /// it. Asserted on the call rather than on the result, because a substituted
    /// file system returns whatever it was told to and would happily hide a
    /// recursive search behind a flat answer.
    /// </remarks>
    [Fact]
    public void Resolve_SearchesTheTopLevelForAssembliesOnly()
    {
        GivenDirectory(Rooted("plugins"), "a.dll");

        Resolve(Rooted("plugins"));

        _fileSystem.Received(1).EnumerateFiles(
            Rooted("plugins"),
            "*.dll",
            SearchOption.TopDirectoryOnly);
    }

    /// <remarks>
    /// Against the workspace root, which is the directory the user invoked the
    /// tool in — so <c>--rules-path./rules</c> means what their shell's own tab
    /// completion just showed them.
    /// </remarks>
    [Fact]
    public void Resolve_WithARelativePath_ResolvesItAgainstTheWorkspaceRoot()
    {
        var expected = Path.Combine(Workspace.FullName, "tools");

        GivenDirectory(expected, "a.dll");

        Resolve("tools").AssemblyPaths.ShouldBe([Path.Combine(expected, "a.dll")]);
    }

    [Fact]
    public void Resolve_WithTwoGivenPaths_ProbesBoth()
    {
        GivenDirectory(Rooted("one"), "a.dll");
        GivenDirectory(Rooted("two"), "b.dll");

        Resolve(Rooted("one"), Rooted("two")).AssemblyPaths
            .ShouldBe([Rooted("one", "a.dll"), Rooted("two", "b.dll")]);
    }

    /// <summary>
    /// The implicit directory is found beside the executable.
    /// </summary>
    /// <remarks>
    /// Beside the executable and never inside the workspace, and that is a
    /// security property rather than a convenience: a workspace is frequently a
    /// checkout the person running <c>preflight</c> did not write, and resolving
    /// <c>rules/</c> against it would execute code committed to the repository
    /// under validation, on the first run, with no flag and no prompt.
    /// </remarks>
    [Fact]
    public void Resolve_WithAnImplicitRulesDirectory_ProbesItWithoutBeingAsked()
    {
        GivenDirectory(Path.Combine(Executable.FullName, "rules"), "a.dll");
        GivenDirectory(Path.Combine(Workspace.FullName, "rules"), "planted.dll");

        Resolve().AssemblyPaths
            .ShouldBe([Path.Combine(Executable.FullName, "rules", "a.dll")]);
    }

    /// <summary>
    /// A directory reached both ways is probed once.
    /// </summary>
    /// <remarks>
    /// Without this, someone who points <c>--rules-path</c> at the directory
    /// that would have been probed anyway gets every one of their rule ids
    /// reported as colliding with itself — a refusal produced entirely by the
    /// tool, over a configuration that is correct.
    /// </remarks>
    [Fact]
    public void Resolve_WithTheSameDirectoryGivenAndImplicit_ProbesItOnce()
    {
        var directory = Path.Combine(Executable.FullName, "rules");

        GivenDirectory(directory, "a.dll");

        Resolve(directory).AssemblyPaths.ShouldBe([Path.Combine(directory, "a.dll")]);
    }

    /// <remarks>
    /// Accumulated, like every other kind of configuration error in this tool.
    /// Somebody who mistyped two paths should be told about two of them.
    /// </remarks>
    [Fact]
    public void Resolve_WithSeveralUnusablePaths_ReportsAllOfThem() =>
        Resolve(Rooted("one"), Rooted("two")).Errors.Count.ShouldBe(2);

    private PluginProbeResult Resolve(params string[] paths) =>
        PluginPathResolution.Resolve(_fileSystem, Workspace, Executable, paths);

    private void GivenDirectory(string path, params string[] fileNames)
    {
        _fileSystem.DirectoryExists(path).Returns(true);
        _fileSystem.EnumerateFiles(path, "*.dll", SearchOption.TopDirectoryOnly)
            .Returns([.. fileNames.Select(name => Path.Combine(path, name))]);
    }

    /// <summary>
    /// An absolute path, spelled the way the running platform spells one.
    /// </summary>
    /// <remarks>
    /// <c>"/plugins"</c> is rooted on Linux and relative to the current drive on
    /// Windows, and the difference would make these assertions compare a path
    /// against a differently normalised copy of itself.
    /// </remarks>
    private static string Rooted(params string[] segments) =>
        Path.Combine([Path.GetTempPath(), .. segments]);
}
