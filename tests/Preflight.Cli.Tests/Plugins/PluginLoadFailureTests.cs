namespace Preflight.Cli.Tests.Plugins;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core;
using Preflight.Core.Plugins;
using Preflight.TestSupport;

/// <summary>
/// The failure modes of plugin loading that need a real
/// file or a real type.
/// </summary>
/// <remarks>
/// The plan says "the three failure modes" and the documents enumerate five —
/// a corrupt assembly, a missing dependency, an incompatible contract, a type
/// that cannot be constructed, and a rule id claimed twice. The count in 17 is
/// stale rather than a smaller scope, and it has been corrected there. Three of
/// the five are decided from a description of an assembly and are asserted in
/// <c>Preflight.Core.Tests</c>; the two here are the ones a description cannot
/// produce.
/// </remarks>
public sealed class PluginLoadFailureTests
{
    /// <summary>
    /// A file that ends in <c>.dll</c> and is not an assembly.
    /// </summary>
    /// <remarks>
    /// Exit 2, naming the file. Without the wrapping this would be a
    /// <c>BadImageFormatException</c> escaping to the command boundary, and
    /// the exit-code contract maps anything that is not a
    /// <see cref="ConfigurationLoadException"/> to 3 — an internal error, which
    /// calls the tool's owner about somebody else's broken deployment.
    /// </remarks>
    [Fact]
    public void Load_WithAFileThatIsNotAnAssembly_IsAConfigurationErrorNamingIt()
    {
        var directory = PluginFixtures.BrokenPluginDirectory();

        try
        {
            using var loader = new PluginAssemblyLoader();

            var exception = Should.Throw<PluginAssemblyUnreadableException>(
                () => loader.Load(Path.Combine(directory.FullName, "Broken.Rules.dll")));

            exception.ShouldBeAssignableTo<ConfigurationLoadException>();
            ExitCode.ForException(exception).ShouldBe(ExitCode.ConfigurationError);
        }
        finally
        {
            PluginFixtures.TryDelete(directory);
        }
    }

    [Fact]
    public void Load_WithAPathThatIsNotThere_IsAConfigurationError()
    {
        using var loader = new PluginAssemblyLoader();

        Should.Throw<PluginAssemblyUnreadableException>(
            () => loader.Load(Path.Combine(Path.GetTempPath(), "preflight-absent", "Nothing.dll")));
    }

    /// <summary>
    /// A type that implements an <c>IValidationRule</c> from a foreign contract
    /// is refused out loud.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The failure plugin loading describes the bug for and never asks anyone to
    /// detect. Without this, the symptom is silence: an assembly that loads,
    /// contributes no rule, and raises nothing — indistinguishable from an empty
    /// directory, and a green run missing everything the production declared.
    /// </para>
    /// <para>
    /// The fixture is a real type from real assemblies, bound to a second copy
    /// of the contract, which is exactly what a broken loader hands the engine.
    /// A hand-written stand-in could not be built: a fake interface would need
    /// the full name <c>Preflight.Abstractions.Rules.IValidationRule</c>, and
    /// declaring that namespace inside a test project makes the real interface
    /// ambiguous for every other file in it.
    /// </para>
    /// </remarks>
    [Fact]
    public void Load_WithATypeBoundToAForeignContract_NamesTheTypeAndBothContracts()
    {
        var foreign = PluginFixtures.ForeignContractRuleType;

        // The fixture is only worth anything if it really is foreign, and this
        // is the assertion that says so. If the isolation ever stopped working,
        // the test below would pass by loading an ordinary rule.
        typeof(IValidationRule).IsAssignableFrom(foreign).ShouldBeFalse();

        var message = Should.Throw<PluginLoadException>(() => Load(foreign)).Message;

        message.ShouldContain(foreign.FullName!);
        message.ShouldContain(typeof(IValidationRule).Assembly.FullName!);
        message.ShouldContain("Private=false");
    }

    /// <summary>
    /// An assembly holding only such types contributes nothing at all.
    /// </summary>
    /// <remarks>
    /// The salvage that looks reasonable and is not: plugin loading refuses a
    /// partial plugin, because a rule set that is silently a subset of what the
    /// policy declared is the false green of principle 7.
    /// </remarks>
    [Fact]
    public void Load_WithAForeignContract_ContributesNoRuleFromThatAssembly() =>
        Should.Throw<PluginLoadException>(
                () => Load(PluginFixtures.ForeignContractRuleType, typeof(SampleCliRule)))
            .Errors.ShouldHaveSingleItem()
            .ShouldBeOfType<PluginLoadError.ForeignAbstractions>();

    private static IReadOnlyList<IValidationRule> Load(params Type[] types)
    {
        var loader = new FakeAssemblyLoader().Containing(new LoadedPluginAssembly
        {
            Path = "/plugins/Acme.Rules.dll",
            AbstractionsReference = AbstractionsCompatibility.HostVersion,
            Types = types,
        });

        return new PluginLoader(loader).Load([], new PluginProbeResult(["/plugins/Acme.Rules.dll"], []));
    }
}

/// <summary>
/// A well-formed rule, so that "the assembly contributed nothing" is a claim
/// about the loader rather than about an empty assembly.
/// </summary>
public sealed class SampleCliRule : IValidationRule
{
    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = new RuleId("acme.content.sample"),
        DisplayName = "Sample",
        Stage = ValidationStage.PreSubmit,
    };

    public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken) =>
        Task.FromResult(RuleOutcome.Passed());
}
