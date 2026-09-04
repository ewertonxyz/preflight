namespace Preflight.Cli.Tests.Plugins;

using System.Reflection;
using System.Runtime.Loader;
using Preflight.Abstractions.Rules;
using Preflight.Core.Plugins;
using Preflight.TestSupport;

/// <summary>
/// The load context of plugin loading, against real files.
/// </summary>
/// <remarks>
/// <para>
/// The one question in plugin loading whose answer <em>is</em> the runtime:
/// whether the <c>IValidationRule</c> a plugin implements is the same type the
/// tool knows. Plugin loading calls getting it wrong one of the most irritating
/// bugs in.NET plugin systems, and the reason it is irritating is that a
/// broken loader produces no error, no log line and no rule — a green run
/// missing everything a production declared.
/// </para>
/// <para>
/// The context is reached through
/// <see cref="AssemblyLoadContext.GetLoadContext(Assembly)"/> rather than by
/// exposing the loader's private class. A type made public so a test can name
/// it is production surface serving a test, which this repository refuses
/// everywhere else.
/// </para>
/// </remarks>
public sealed class PluginLoadContextTests : IDisposable
{
    private readonly DirectoryInfo _plugins;
    private readonly PluginAssemblyLoader _loader = new();

    public PluginLoadContextTests() => _plugins = PluginFixtures.PluginDirectory();

    public void Dispose()
    {
        _loader.Dispose();
        PluginFixtures.TryDelete(_plugins);
    }

    /// <summary>
    /// The contract a plugin binds to is the tool's own assembly instance.
    /// </summary>
    /// <remarks>
    /// Reference equality on the <see cref="Assembly"/> object, which is the
    /// sharpest assertion available: two copies of the same file have the same
    /// name, the same version and the same types, and are different types to the
    /// runtime.
    /// </remarks>
    [Fact]
    public void LoadContext_ResolvesTheContract_ToTheHostAssemblyInstance() =>
        ContextOf(Load(_plugins))
            .LoadFromAssemblyName(new AssemblyName(AbstractionsCompatibility.AssemblyName))
            .ShouldBeSameAs(typeof(IValidationRule).Assembly);

    /// <summary>
    /// It still does when the plugin shipped its own copy of the contract.
    /// </summary>
    /// <remarks>
    /// The negative control, and the test that makes the one above falsifiable.
    /// The sample references the contract with <c>Private="false"</c>, so no
    /// copy is deployed beside it — and with no copy there, a loader that
    /// delegated correctly and one that did nothing at all behave identically.
    /// This is the case the delegation exists for: a plugin author who left the
    /// reference at its default, which is the normal default and therefore the
    /// normal mistake.
    /// </remarks>
    [Fact]
    public void LoadContext_WithTheContractShippedBesideThePlugin_StillResolvesToTheHostInstance()
    {
        var shipped = PluginFixtures.PluginDirectory(withContractCopy: true);

        try
        {
            File.Exists(Path.Combine(shipped.FullName, "Preflight.Abstractions.dll")).ShouldBeTrue();

            var loaded = Load(shipped);

            ContextOf(loaded)
                .LoadFromAssemblyName(new AssemblyName(AbstractionsCompatibility.AssemblyName))
                .ShouldBeSameAs(typeof(IValidationRule).Assembly);

            // And therefore the rule is a rule, rather than a type that merely
            // looks like one. This is the observable consequence, and it is what
            // the whole detector in PluginLoader exists to notice the absence of.
            loaded.Types.ShouldContain(type => typeof(IValidationRule).IsAssignableFrom(type));
        }
        finally
        {
            PluginFixtures.TryDelete(shipped);
        }
    }

    /// <summary>
    /// Every plugin gets a collectible context of its own.
    /// </summary>
    /// <remarks>
    /// Plugin loading asks for collectible, and this asserts the property rather
    /// than the collection. Whether the runtime has actually reclaimed the
    /// memory is not observable without a garbage collection whose timing
    /// nothing promises, and a test that waited for one would be the flakiest in
    /// the repository — the same trade this project already made when it stopped
    /// measuring the scheduler in a cancellation test.
    /// </remarks>
    [Fact]
    public void LoadContext_IsCollectibleAndItsOwn()
    {
        using var second = new PluginAssemblyLoader();

        var context = ContextOf(Load(_plugins));

        context.IsCollectible.ShouldBeTrue();
        context.ShouldNotBeSameAs(AssemblyLoadContext.Default);
        ContextOf(second.Load(PluginFixtures.SampleAssemblyIn(_plugins))).ShouldNotBeSameAs(context);
    }

    /// <summary>
    /// Releasing the loader really unloads, rather than merely dropping a
    /// reference.
    /// </summary>
    /// <remarks>
    /// Immediate and deterministic: a context that has been unloaded refuses
    /// further loads at once, whatever the collector has or has not got round
    /// to. That is the half of "collectible" a caller can be held to, and it is
    /// the half a leak would break.
    /// </remarks>
    [Fact]
    public void LoadContext_AfterTheLoaderIsReleased_RefusesFurtherLoads()
    {
        var loader = new PluginAssemblyLoader();
        var context = ContextOf(loader.Load(PluginFixtures.SampleAssemblyIn(_plugins)));

        loader.Dispose();

        Should.Throw<InvalidOperationException>(
            () => context.LoadFromAssemblyPath(PluginFixtures.SampleAssemblyIn(_plugins)));
    }

    /// <summary>
    /// The sample's own output carries no copy of the contract.
    /// </summary>
    /// <remarks>
    /// The cheapest assertion in the phase and one of the most valuable: it is
    /// what turns <c>Private="false"</c> in plugin loading from a sentence in a
    /// document into something that cannot be lost silently. Deleting the
    /// attribute leaves every other test in this file green, because the tests
    /// above prove the loader copes with a shipped contract — this is the one
    /// that notices the plugin started shipping one.
    /// </remarks>
    [Fact]
    public void SampleOutput_DoesNotCarryTheContractAssembly() =>
        File.Exists(Path.Combine(_plugins.FullName, "Preflight.Abstractions.dll")).ShouldBeFalse();

    /// <remarks>
    /// The sample declares its contract reference, which is what the version
    /// check of the plugin version contract reads. An assembly whose reference could not be
    /// found would be treated as "not a plugin" and skipped in silence, so this
    /// pins the input that decision is made from.
    /// </remarks>
    [Fact]
    public void Load_ReadsTheContractVersionThePluginWasBuiltAgainst() =>
        Load(_plugins).AbstractionsReference.ShouldBe(AbstractionsCompatibility.HostVersion);

    /// <summary>
    /// An assembly in the directory that is not a plugin reports no contract
    /// version.
    /// </summary>
    /// <remarks>
    /// The input to the other side of that decision, and the case is not
    /// hypothetical: a plugin author who left the reference at its default ships
    /// <c>Preflight.Abstractions.dll</c> into the same directory, and the probe
    /// enumerates every <c>.dll</c> it finds. The contract assembly does not
    /// reference itself, so it is correctly read as "not a plugin" and skipped —
    /// which is what stops a rules directory from being unusable for the helper
    /// libraries a real rule depends on.
    /// </remarks>
    [Fact]
    public void Load_WithAnAssemblyThatIsNotAPlugin_ReportsNoContractVersion()
    {
        var directory = PluginFixtures.PluginDirectory(withContractCopy: true);

        try
        {
            _loader
                .Load(Path.Combine(directory.FullName, "Preflight.Abstractions.dll"))
                .AbstractionsReference
                .ShouldBeNull();
        }
        finally
        {
            PluginFixtures.TryDelete(directory);
        }
    }

    private LoadedPluginAssembly Load(DirectoryInfo directory) =>
        _loader.Load(PluginFixtures.SampleAssemblyIn(directory));

    private static AssemblyLoadContext ContextOf(LoadedPluginAssembly assembly) =>
        AssemblyLoadContext.GetLoadContext(assembly.Types[0].Assembly)!;
}
