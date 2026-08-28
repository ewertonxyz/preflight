namespace Preflight.Cli.Tests;

using Preflight.Abstractions;
using Preflight.Core;
using Preflight.Core.Plugins;
using Preflight.Core.Policy;

/// <summary>
/// Fixes the exit code table of the exit-code contract.
/// </summary>
/// <remarks>
/// The table is the tool's contract with a pipeline, and the row that matters
/// most is the one separating 2 from 1: broken configuration calls the tool's
/// owner, rejected code calls the commit's author. A defect here misroutes an
/// incident silently and forever, which is why every row is pinned rather than
/// the interesting ones.
/// </remarks>
public sealed class ExitCodeTests
{
    [Theory]
    [InlineData(RunVerdict.Passed, 0)]
    [InlineData(RunVerdict.PassedWithWarnings, 0)]
    [InlineData(RunVerdict.Blocked, 1)]
    [InlineData(RunVerdict.Errored, 3)]
    public void ForVerdict_MatchesTheDocumentedTable(RunVerdict verdict, int expected)
    {
        ExitCode.ForVerdict(verdict).ShouldBe(expected);
    }

    /// <remarks>
    /// Every subtype is passed through the same call on purpose. Each one
    /// individually would pass against a <c>catch</c> written per subtype; only
    /// all of them together prove the mapping keys on the shared base, which is
    /// what <c>ConfigurationLoadException</c>'s own remarks say it exists for —
    /// and which is exactly what the plugin loader relied on when it added two more
    /// without touching <see cref="ExitCode.ForException"/> at all.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ConfigurationErrors))]
    public void ForException_ForEveryConfigurationLoadException_IsTwo(ConfigurationLoadException exception)
    {
        ExitCode.ForException(exception).ShouldBe(2);
    }

    public static TheoryData<ConfigurationLoadException> ConfigurationErrors() =>
    [
        new PolicyValidationException([
            new PolicyValidationError("Unknown key 'blockng'.", "preflight.base.json", 4, "blockng"),
        ]),
        new GraphValidationException([
            new GraphValidationError.SelfDependency(new RuleId("core.workspace.toolchain")),
        ]),
        new RuleDiscoveryException("No rule types were found in the supplied assemblies."),
        new PluginLoadException([
            new PluginLoadError.PluginPathUnusable("./rules", "does not exist"),
        ]),
        new PluginAssemblyUnreadableException("'Broken.Rules.dll' is not a managed assembly."),
    ];

    /// <remarks>
    /// The concurrency contract: a cancelled run is <c>Errored</c>, and 8.4 maps that to 3.
    /// Cancellation gets no code of its own — the verdict already carries the
    /// finer distinction, and a fifth code would have to be added to the table
    /// every consumer reads.
    /// </remarks>
    [Fact]
    public void ForException_ForCancellation_IsThree()
    {
        ExitCode.ForException(new OperationCanceledException()).ShouldBe(3);
    }

    [Fact]
    public void ForException_ForAnUnexpectedException_IsThree()
    {
        ExitCode.ForException(new InvalidOperationException("boom")).ShouldBe(3);
    }

    /// <remarks>
    /// Guards the arm that only fires if someone adds a fifth
    /// <see cref="RunVerdict"/> without visiting this table. Silently returning
    /// 0 for an unmapped verdict is the false green the whole tool argues
    /// against, so the switch throws rather than defaulting.
    /// </remarks>
    [Fact]
    public void ForVerdict_WithAValueOutsideTheEnum_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ExitCode.ForVerdict((RunVerdict)99));
    }

    /// <summary>
    /// Every configuration error, whatever its subtype, is exit 2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mapping catches the base and never a subtype, so this passes today
    /// by construction. It is written anyway, and by reflection rather than by
    /// a hand-written list, because the phases after this one add refusals —
    /// a violated seal, an ambiguous pipeline, a manifest that already exists —
    /// and the cost of getting one of them wrong is not cosmetic: 2 calls the
    /// owner of the tool, 3 says the tool itself broke, and a refusal that
    /// exits 3 sends the wrong person to look.
    /// </para>
    /// <para>
    /// Uninitialised instances, because the subtypes carry different
    /// constructors — an error list, a path, a load failure — and none of that
    /// is what the mapping reads.
    /// </para>
    /// </remarks>
    [Fact]
    public void ForException_ForEverySubtypeOfConfigurationLoadException_IsConfigurationError()
    {
        var subtypes = typeof(ConfigurationLoadException).Assembly.GetTypes()
            .Concat(typeof(ExitCode).Assembly.GetTypes())
            .Where(type => type is { IsAbstract: false } && typeof(ConfigurationLoadException).IsAssignableFrom(type))
            .ToArray();

        subtypes.ShouldNotBeEmpty();

        foreach (var subtype in subtypes)
        {
            var instance = (Exception)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(subtype);

            ExitCode.ForException(instance).ShouldBe(
                ExitCode.ConfigurationError,
                $"{subtype.Name} is a configuration error and must exit 2.");
        }
    }
}
