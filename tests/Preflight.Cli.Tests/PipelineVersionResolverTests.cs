namespace Preflight.Cli.Tests;

using NSubstitute;
using Preflight.Core.Policy;

/// <summary>
/// Fixes which installed package version a run resolves to.
/// </summary>
/// <remarks>
/// Pure, so every row is exercised without a disk. The two refusals worth
/// reading twice are a pin outside the checkout's range — which never quietly
/// switches — and a checkout that both requires a package and carries its own
/// policy file. See ADR-032.
/// </remarks>
public sealed class PipelineVersionResolverTests
{
    private static readonly PipelineInstallRoot Root =
        new(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "preflight-install-root")));

    [Fact]
    public void Resolve_WithNoPipelineSelected_IsNoPackage() =>
        Resolve(Reader(), selection: new PipelineSelection(null, PipelineSource.None)).ShouldBeNull();

    [Fact]
    public void Resolve_WithAWorkspacePolicyFile_IsNoPackage() =>
        Resolve(
            Reader("1.4.0"),
            state: Pinned("projecta", "1.4.0"),
            workspacePolicyExists: true)
        .ShouldBeNull();

    [Fact]
    public void Resolve_WithARequirementAndAWorkspaceFile_Throws()
    {
        var error = Should.Throw<PolicyValidationException>(
            () => Resolve(
                Reader("1.4.0"),
                requirement: Requirement("1.0.0"),
                workspacePolicyExists: true));

        error.Message.ShouldContain("preflight.projecta.json");
        error.Message.ShouldContain("requiresPipeline");
    }

    [Fact]
    public void Resolve_WithAPinInsideTheRange_UsesThePin()
    {
        var resolved = Resolve(
            Reader("1.2.0", "1.4.0"),
            state: Pinned("projecta", "1.4.0"),
            requirement: Requirement("1.2.0", "2.0.0"));

        resolved!.Version.ToString().ShouldBe("1.4.0");
        resolved.Source.ShouldBe(PipelineVersionSource.Pin);
    }

    [Fact]
    public void Resolve_WithAPinAndNoRequirement_UsesThePin()
    {
        var resolved = Resolve(Reader("1.2.0", "1.4.0"), state: Pinned("projecta", "1.2.0"));

        resolved!.Version.ToString().ShouldBe("1.2.0");
        resolved.Source.ShouldBe(PipelineVersionSource.Pin);
    }

    [Fact]
    public void Resolve_WithAPinOutsideTheRange_ThrowsNamingThePinTheRangeAndTheUseCommand()
    {
        var error = Should.Throw<PolicyValidationException>(
            () => Resolve(
                Reader("1.1.0", "1.2.0", "1.4.0"),
                state: Pinned("projecta", "1.1.0"),
                requirement: Requirement("1.2.0", "2.0.0")));

        error.Message.ShouldContain("1.1.0");
        error.Message.ShouldContain("1.2.0");
        error.Message.ShouldContain("2.0.0");
        error.Message.ShouldContain("preflight pipeline use projecta@1.4.0");
    }

    /// <summary>
    /// The same refusal with nothing to point at says so instead of naming a
    /// version that does not exist.
    /// </summary>
    /// <remarks>
    /// The remedy half of that message is composed from the installed versions
    /// that satisfy the range, and there is not always one. Left unguarded it
    /// would print <c>preflight pipeline use projecta@</c> with nothing after
    /// the separator — a command somebody would paste and be refused for a
    /// second, unrelated reason.
    /// </remarks>
    [Fact]
    public void Resolve_WithAPinOutsideTheRangeAndNothingElseThatSatisfiesIt_SaysToInstallOne()
    {
        var error = Should.Throw<PolicyValidationException>(
            () => Resolve(
                Reader("1.1.0"),
                state: Pinned("projecta", "1.1.0"),
                requirement: Requirement("1.2.0", "2.0.0")));

        error.Message.ShouldContain("install one first");
        error.Message.ShouldNotContain("preflight pipeline use projecta@ ");
    }

    [Fact]
    public void Resolve_WithAPinPointingAtADeletedDirectory_ThrowsRatherThanFallingBackToNewest()
    {
        var error = Should.Throw<PolicyValidationException>(
            () => Resolve(Reader("1.2.0", "1.4.0"), state: Pinned("projecta", "1.3.0")));

        error.Message.ShouldContain("1.3.0");
        error.Message.ShouldContain("not installed");
    }

    [Fact]
    public void Resolve_WithoutAPinAndARequirement_TakesTheNewestThatSatisfies()
    {
        var resolved = Resolve(
            Reader("1.2.0", "1.4.0", "2.1.0"),
            requirement: Requirement("1.2.0", "2.0.0"));

        resolved!.Version.ToString().ShouldBe("1.4.0");
        resolved.Source.ShouldBe(PipelineVersionSource.Requirement);
    }

    [Fact]
    public void Resolve_WithoutAPinOrARequirement_TakesTheNewestInstalled()
    {
        var resolved = Resolve(Reader("1.9.0", "1.10.0"));

        resolved!.Version.ToString().ShouldBe("1.10.0");
        resolved.Source.ShouldBe(PipelineVersionSource.Newest);
    }

    [Fact]
    public void Resolve_WithARequirementAndNothingThatSatisfies_ThrowsNamingWhatIsInstalled()
    {
        var error = Should.Throw<PolicyValidationException>(
            () => Resolve(Reader("1.4.0"), requirement: Requirement("2.0.0")));

        error.Message.ShouldContain("2.0.0");
        error.Message.ShouldContain("1.4.0");
    }

    [Fact]
    public void Resolve_WithARequirementAndNothingInstalled_Throws()
    {
        var error = Should.Throw<PolicyValidationException>(
            () => Resolve(Reader(), requirement: Requirement("1.0.0")));

        error.Message.ShouldContain("nothing is installed");
    }

    [Fact]
    public void Resolve_WithoutARequirementAndNothingInstalled_IsNoPackage() =>
        Resolve(Reader()).ShouldBeNull();

    /// <remarks>
    /// The case that forbade a machine-wide pipeline selection. One machine, one
    /// install root, one pin map, two checkouts — and each has to answer for
    /// itself. A pin held per machine rather than per pipeline name passes every
    /// test above and fails this one.
    /// </remarks>
    [Fact]
    public void Resolve_ForTwoWorkspacesOnOneMachine_ResolvesEachIndependently()
    {
        var reader = Substitute.For<IInstalledPipelineReader>();
        reader.Versions("projecta").Returns([Version("1.9.0"), Version("1.10.0")]);
        reader.Versions("projectb").Returns([Version("2.0.0")]);

        var state = new MachineState
        {
            Pins = new Dictionary<string, PackageVersion>(StringComparer.OrdinalIgnoreCase)
            {
                ["projecta"] = Version("1.9.0"),
            },
            Keep = MachineState.DefaultKeep,
        };

        var a = PipelineVersionResolver.Resolve(
            Root, reader, state, new PipelineSelection("projecta", PipelineSource.Checkout), null, false);

        var b = PipelineVersionResolver.Resolve(
            Root, reader, state, new PipelineSelection("projectb", PipelineSource.Checkout), null, false);

        a!.Version.ToString().ShouldBe("1.9.0");
        a.Source.ShouldBe(PipelineVersionSource.Pin);
        b!.Version.ToString().ShouldBe("2.0.0");
        b.Source.ShouldBe(PipelineVersionSource.Newest);
    }

    private static InstalledPipeline? Resolve(
        IInstalledPipelineReader reader,
        MachineState? state = null,
        PipelineSelection? selection = null,
        PipelineRequirement? requirement = null,
        bool workspacePolicyExists = false) =>
        PipelineVersionResolver.Resolve(
            Root,
            reader,
            state ?? MachineState.Empty,
            selection ?? new PipelineSelection("projecta", PipelineSource.Checkout),
            requirement,
            workspacePolicyExists);

    private static IInstalledPipelineReader Reader(params string[] versions)
    {
        var reader = Substitute.For<IInstalledPipelineReader>();

        reader.Versions(Arg.Any<string>()).Returns([.. versions.Select(Version)]);

        return reader;
    }

    private static MachineState Pinned(string name, string version) => new()
    {
        Pins = new Dictionary<string, PackageVersion>(StringComparer.OrdinalIgnoreCase)
        {
            [name] = Version(version),
        },
        Keep = MachineState.DefaultKeep,
    };

    private static PipelineRequirement Requirement(string minimum, string? maximum = null) =>
        new(Version(minimum), maximum is null ? null : Version(maximum));

    private static PackageVersion Version(string text)
    {
        PackageVersion.TryParse(text, out var version).ShouldBeTrue();

        return version!;
    }
}
