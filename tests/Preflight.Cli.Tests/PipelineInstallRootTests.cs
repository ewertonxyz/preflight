namespace Preflight.Cli.Tests;

using NSubstitute;
using Preflight.Abstractions.Rules;
using Preflight.Core.Policy;

/// <summary>
/// Fixes where installed packages are looked for.
/// </summary>
/// <remarks>
/// A machine fact read out of the environment, so every branch is exercised
/// through a substituted reader rather than by setting variables on the process
/// running the tests. See ADR-032.
/// </remarks>
public sealed class PipelineInstallRootTests : IDisposable
{
    private readonly DirectoryInfo _workspace =
        Directory.CreateTempSubdirectory("preflight-install-root-tests-");

    public void Dispose() => _workspace.Delete(recursive: true);

    [Fact]
    public void Resolve_WithPreflightHomeSet_UsesIt() =>
        Resolve(home: @"D:\pf", localAppData: @"C:\Users\x\AppData\Local")
            .Root.FullName.ShouldBe(@"D:\pf");

    [Fact]
    public void Resolve_WithoutPreflightHome_FallsBackToLocalAppData() =>
        Resolve(home: null, localAppData: @"C:\Users\x\AppData\Local")
            .Root.FullName.ShouldBe(@"C:\Users\x\AppData\Local\Preflight");

    /// <remarks>
    /// An exported-but-empty variable is a shell artefact and not an
    /// instruction, which is the rule the CI detection already applies.
    /// </remarks>
    [Fact]
    public void Resolve_WithAnEmptyPreflightHome_TreatsItAsAbsent() =>
        Resolve(home: string.Empty, localAppData: @"C:\Users\x\AppData\Local")
            .Root.FullName.ShouldBe(@"C:\Users\x\AppData\Local\Preflight");

    /// <remarks>
    /// The state of every container without a Windows profile. A path built out
    /// of null would throw somewhere below and exit 3, sending the tool's owner
    /// to look at somebody else's missing environment.
    /// </remarks>
    [Fact]
    public void Resolve_WithNeitherVariable_ThrowsNamingBoth()
    {
        var error = Should.Throw<PolicyValidationException>(() => Resolve(null, null));

        error.Message.ShouldContain(PipelineInstallRoot.HomeVariable);
        error.Message.ShouldContain(PipelineInstallRoot.LocalAppDataVariable);
    }

    [Fact]
    public void Resolve_WithARelativePath_Throws() =>
        Should.Throw<PolicyValidationException>(() => Resolve(home: "pipelines", localAppData: null));

    /// <remarks>
    /// The argument ADR-023 nº5 makes about <c>cachePath</c>, sharpened: rule
    /// assemblies would be loaded out of the tree being validated, which is
    /// exactly what the implicit <c>rules/</c> directory refuses to do.
    /// </remarks>
    [Fact]
    public void Resolve_WhenTheRootContainsTheWorkspace_Throws()
    {
        var error = Should.Throw<PolicyValidationException>(
            () => Resolve(home: _workspace.Parent!.FullName, localAppData: null));

        error.Message.ShouldContain(_workspace.FullName);
    }

    [Fact]
    public void Resolve_WhenTheRootIsTheWorkspace_Throws() =>
        Should.Throw<PolicyValidationException>(
            () => Resolve(home: _workspace.FullName, localAppData: null));

    [Fact]
    public void PipelineDirectory_ForANameThatIsNotALabel_Throws() =>
        Should.Throw<PolicyValidationException>(
            () => new PipelineInstallRoot(_workspace).PipelineDirectory("../evil"));

    [Fact]
    public void VersionDirectory_IsTheNameThenTheVersion()
    {
        PackageVersion.TryParse("1.4.0", out var version).ShouldBeTrue();

        new PipelineInstallRoot(new DirectoryInfo(@"D:\pf"))
            .VersionDirectory("projecta", version!)
            .FullName
            .ShouldBe(@"D:\pf\pipelines\projecta\1.4.0");
    }

    /// <summary>
    /// A relative path in either variable is refused, and the refusal names the
    /// variable it came from.
    /// </summary>
    /// <remarks>
    /// Naming the right one is the whole value of the message. A person with
    /// both variables set, told only that "the install root is not absolute",
    /// has two places to look and no reason to prefer either — and
    /// <c>PREFLIGHT_HOME</c> is the one they set themselves, so it is the one
    /// they can fix.
    /// </remarks>
    [Theory]
    [InlineData("relative/home", null, PipelineInstallRoot.HomeVariable)]
    [InlineData(null, "relative/appdata", PipelineInstallRoot.LocalAppDataVariable)]
    [InlineData("relative/home", @"D:\appdata", PipelineInstallRoot.HomeVariable)]
    public void Resolve_WithARelativePath_RefusesNamingTheVariableItCameFrom(
        string? home, string? localAppData, string expected) =>
        Should.Throw<PolicyValidationException>(() => Resolve(home, localAppData))
            .Message.ShouldContain(expected);

    private PipelineInstallRoot Resolve(string? home, string? localAppData)
    {
        var reader = Substitute.For<IEnvironmentReader>();

        reader.GetVariable(Arg.Any<string>()).Returns((string?)null);
        reader.GetVariable(PipelineInstallRoot.HomeVariable).Returns(home);
        reader.GetVariable(PipelineInstallRoot.LocalAppDataVariable).Returns(localAppData);

        return PipelineInstallRoot.Resolve(reader, _workspace);
    }
}
