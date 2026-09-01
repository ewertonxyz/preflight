namespace Preflight.Cli.Tests;

/// <summary>
/// Fixes how this machine remembers its pins.
/// </summary>
/// <remarks>
/// Against a real temporary directory, because what this type does is read and
/// replace a file and the replacement semantics are the point. The write test
/// below is the deliberate opposite of
/// <c>WorkspaceFileWriterTests</c>'s refusal to replace: one guards a file a
/// person authored, the other holds a value whose whole purpose is to change. A
/// refactor that unifies the two has to break one of the pair. See ADR-033.
/// </remarks>
public sealed class MachineStateStoreTests : IDisposable
{
    private readonly DirectoryInfo _root =
        Directory.CreateTempSubdirectory("preflight-machine-state-");

    private readonly MachineStateStore _store = new();

    public void Dispose() => _root.Delete(recursive: true);

    private string Path => System.IO.Path.Combine(_root.FullName, "machine.json");

    private void Write(string content) => File.WriteAllText(Path, content);

    [Fact]
    public void Read_WhenTheFileIsAbsent_IsNoPinsAndKeepTen()
    {
        var state = _store.Read(Path);

        state.Pins.ShouldBeEmpty();
        state.Keep.ShouldBe(MachineState.DefaultKeep);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    public void Read_ForEveryUnusableShape_ThrowsNamingTheFile(string content)
    {
        Write(content);

        Should.Throw<MachineStateException>(() => _store.Read(Path))
            .Message.ShouldContain("machine.json");
    }

    [Fact]
    public void Read_WithAPinThatIsNotAVersion_Throws()
    {
        Write("""{ "pins": { "projecta": "1.4" } }""");

        Should.Throw<MachineStateException>(() => _store.Read(Path));
    }

    [Fact]
    public void Read_WithAPinnedNameThatIsNotALabel_Throws()
    {
        Write("""{ "pins": { "../evil": "1.4.0" } }""");

        Should.Throw<MachineStateException>(() => _store.Read(Path));
    }

    /// <remarks>
    /// The name becomes a directory on a file system that does not distinguish
    /// case, so the two entries address one package and the dictionary would
    /// take whichever was read last.
    /// </remarks>
    [Fact]
    public void Read_WithTwoNamesDifferingOnlyInCase_Throws()
    {
        Write("""{ "pins": { "projecta": "1.4.0", "ProjectA": "1.5.0" } }""");

        Should.Throw<MachineStateException>(() => _store.Read(Path));
    }

    /// <remarks>
    /// An ordinal dictionary over a case-insensitive disk makes a pin that
    /// exists and is not found, and the run then falls to the newest installed
    /// version with nothing printed about it.
    /// </remarks>
    [Fact]
    public void Pins_LookUpIsOrdinalIgnoreCase()
    {
        Write("""{ "pins": { "ProjectA": "1.4.0" } }""");

        _store.Read(Path).Pins.TryGetValue("projecta", out var version).ShouldBeTrue();
        version!.ToString().ShouldBe("1.4.0");
    }

    [Fact]
    public void Read_WithANegativeKeep_Throws()
    {
        Write("""{ "keep": -1 }""");

        Should.Throw<MachineStateException>(() => _store.Read(Path));
    }

    [Fact]
    public void Write_OverAnExistingFile_ReplacesItAndLeavesNoStagingFile()
    {
        PackageVersion.TryParse("1.4.0", out var version).ShouldBeTrue();

        _store.Write(Path, MachineState.Empty);
        _store.Write(Path, MachineState.Empty with
        {
            Pins = new Dictionary<string, PackageVersion>(StringComparer.OrdinalIgnoreCase)
            {
                ["projecta"] = version!,
            },
        });

        _store.Read(Path).Pins["projecta"].ToString().ShouldBe("1.4.0");
        _root.GetFiles().Select(file => file.Name).ShouldBe(["machine.json"]);
    }

    [Fact]
    public void Write_RoundTripsKeep()
    {
        _store.Write(Path, MachineState.Empty with { Keep = 3 });

        _store.Read(Path).Keep.ShouldBe(3);
    }

    /// <remarks>
    /// A file holding the four bytes <c>null</c> is valid JSON that deserialises
    /// to nothing, which is the one malformed shape that does not raise on the
    /// way in. Left unchecked it would dereference as an empty state — every pin
    /// silently forgotten, and the next run taking the newest installed version
    /// instead of the pinned one with nothing printed about it.
    /// </remarks>
    [Fact]
    public void Read_WhenTheFileHoldsTheLiteralNull_ThrowsRatherThanReadingAsEmpty()
    {
        File.WriteAllText(Path, "null");

        Should.Throw<MachineStateException>(() => _store.Read(Path))
            .Message.ShouldContain(Path);
    }

    /// <summary>
    /// A write that cannot complete leaves no staging file behind.
    /// </summary>
    /// <remarks>
    /// The install root is a directory a person opens in a file manager, and a
    /// scattering of eight-character temporary names in it is the sort of thing
    /// somebody finds a year later and cannot attribute. A directory standing
    /// where the file goes is the cheapest way to make the move fail without
    /// touching permissions.
    /// </remarks>
    [Fact]
    public void Write_WhenTheMoveFails_RemovesTheStagingFileAndPropagates()
    {
        Directory.CreateDirectory(Path);

        // UnauthorizedAccessException derives from SystemException and not from
        // IOException, and which of the two a directory in the way produces is
        // the file system's business rather than this store's. What the store
        // promises is that it does not swallow it and does not leave the staging
        // file behind.
        Should.Throw<Exception>(() => _store.Write(Path, MachineState.Empty))
            .ShouldBeAssignableTo<SystemException>();

        _root.GetFiles().ShouldBeEmpty();
    }
}
