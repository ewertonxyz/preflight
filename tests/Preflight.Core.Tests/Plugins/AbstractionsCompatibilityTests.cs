namespace Preflight.Core.Tests.Plugins;

using Preflight.Abstractions.Rules;
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
    /// <para>
    /// The first three rows are the version contract's own table. The rest are
    /// the borders it leaves unstated, and each is a decision somebody would
    /// otherwise make by accident when they touched the comparison:
    /// </para>
    /// <list type="bullet">
    /// <item>the exact same version, which a naive strict comparison refuses;</item>
    /// <item>patch in either direction, which the contract defines as
    /// documentation and therefore as something that must never decide;</item>
    /// <item>a host newer by a major, which the table shows only the other way
    /// round;</item>
    /// <item>a plugin from a 0.x line against a 1.x host, which is a different
    /// contract in the ordinary way.</item>
    /// </list>
    /// <para>
    /// The last three rows are the ones a pre-1.0 tool turns from theory into
    /// the everyday case. While the major is 0, SemVer moves the breaking axis
    /// to the minor, so 0.1 and 0.2 are as unrelated as 1.x and 2.x — and the
    /// asymmetry that lets 1.2.0 load on 1.4.0 does <em>not</em> carry over:
    /// 0.1.0 on 0.2.0 is refused in both directions. A comparison written as
    /// <c>plugin.Major == host.Major</c> passes every row above and fails
    /// exactly these, which is why they are here.
    /// </para>
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
    [InlineData("0.1.0", "0.1.1", true)]
    [InlineData("0.1.0", "0.2.0", false)]
    [InlineData("0.2.0", "0.1.0", false)]
    public void IsCompatible_AcrossTheVersionMatrix_MatchesTheRefusalTable(
        string plugin,
        string host,
        bool expected) =>
        AbstractionsCompatibility.IsCompatible(Version.Parse(plugin), Version.Parse(host))
            .ShouldBe(expected);

    /// <summary>
    /// What a version answers when asked which unbroken line it belongs to.
    /// </summary>
    /// <remarks>
    /// Pinned on its own rather than only through <c>IsCompatible</c>, because
    /// the cache key reads it too and the two callers must agree about which
    /// contracts are the same. A generation that changed shape — <c>0.1.1</c>
    /// answering <c>0.1.1</c> instead of <c>0.1</c>, say — would still satisfy
    /// every compatibility row above while quietly invalidating the cache on
    /// every patch release.
    /// </remarks>
    [Theory]
    [InlineData("1.4.0", "1")]
    [InlineData("1.0.0", "1")]
    [InlineData("2.7.3", "2")]
    [InlineData("0.1.1", "0.1")]
    [InlineData("0.2.0", "0.2")]
    [InlineData("0.0.5", "0.0")]
    public void GenerationOf_NamesTheLineTwoVersionsShareOrDoNot(string version, string expected) =>
        AbstractionsCompatibility.GenerationOf(Version.Parse(version)).ShouldBe(expected);

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
    /// The version the tool advertises is the one its contract assembly
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
