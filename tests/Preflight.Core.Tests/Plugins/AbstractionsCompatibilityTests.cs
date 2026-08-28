namespace Preflight.Core.Tests.Plugins;

using Preflight.Abstractions;
using Preflight.Core.Plugins;

/// <summary>
/// The refusal table of the plugin version contract, row by row.
/// </summary>
/// <remarks>
/// Every row is an input pair rather than an assembly, which is the whole
/// reason the comparison is a function of two versions. The alternative — one
/// built plugin per row — would need a project per row, each of them a copy of
/// the same source under a different version number, and it would test the
/// build system rather than the rule.
/// </remarks>
public sealed class AbstractionsCompatibilityTests
{
    /// <remarks>
    /// The first three rows are the plugin version contract's own table. The rest are the
    /// borders it leaves unstated, and each is a decision somebody would
    /// otherwise make by accident when they touched the comparison:
    /// <list type="bullet">
    /// <item>the exact same version, which a naive strict comparison refuses;</item>
    /// <item>patch in either direction, which 11.2 defines as documentation and
    /// therefore as something that must never decide;</item>
    /// <item>a host newer by a major, which the table shows only the other way
    /// round;</item>
    /// <item>a pre-1.0 plugin, which needs no special arm because 0.x is a
    /// different major from 1.x and the first condition already refuses it.</item>
    /// </list>
    /// </remarks>
    [Theory]
    [InlineData("1.2.0", "1.4.0", true)]
    [InlineData("1.4.0", "1.2.0", false)]
    [InlineData("2.0.0", "1.9.9", false)]
    [InlineData("1.2.0", "1.2.0", true)]
    [InlineData("1.2.0", "1.2.5", true)]
    [InlineData("1.2.5", "1.2.0", true)]
    [InlineData("1.0.0", "2.0.0", false)]
    [InlineData("0.9.0", "1.0.0", false)]
    public void IsCompatible_AcrossTheVersionMatrix_MatchesSection112(
        string plugin,
        string host,
        bool expected) =>
        AbstractionsCompatibility.IsCompatible(Version.Parse(plugin), Version.Parse(host))
            .ShouldBe(expected);

    /// <summary>
    /// A refusal names both versions, the plugin, and what would fix it.
    /// </summary>
    /// <remarks>
    /// "Incompatible version" on its own leaves the reader unable to tell which
    /// side is behind, which is the only thing that decides whether they rebuild
    /// a plugin or upgrade the tool.
    /// </remarks>
    [Fact]
    public void RefusalFor_NamesThePluginBothVersionsAndTheAction()
    {
        var message = AbstractionsCompatibility.RefusalFor(
            "/plugins/Acme.Rules.dll",
            new Version(1, 4, 0),
            new Version(1, 2, 0));

        message.ShouldContain("/plugins/Acme.Rules.dll");
        message.ShouldContain("1.4.0");
        message.ShouldContain("1.2.0");
        message.ShouldContain("Rebuild");
    }

    /// <summary>
    /// The version the engine advertises is the one its contract assembly
    /// carries.
    /// </summary>
    /// <remarks>
    /// Read once and cached, so a defect here is a refusal table computed
    /// against the wrong number for the life of the process rather than for one
    /// call.
    /// </remarks>
    [Fact]
    public void HostVersion_IsTheVersionOfTheContractAssembly() =>
        AbstractionsCompatibility.HostVersion
            .ShouldBe(typeof(IValidationRule).Assembly.GetName().Version!);
}
