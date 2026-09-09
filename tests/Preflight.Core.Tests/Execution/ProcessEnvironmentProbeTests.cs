namespace Preflight.Core.Tests.Execution;

using Preflight.Core.Execution;

/// <summary>
/// The shipped environment probe, against the real process block.
/// </summary>
/// <remarks>
/// Nothing here writes a variable, and that is the constraint the whole class
/// is shaped by. A process's environment is global state with no teardown, and
/// this assembly runs its classes concurrently — a test that set one would
/// change the answer another class was in the middle of reading. So the
/// arrangements are a name the operating system always sets and a name nothing
/// could plausibly set.
/// </remarks>
public sealed class ProcessEnvironmentProbeTests
{
    /// <summary>
    /// The one variable every platform this tool runs on defines.
    /// </summary>
    /// <remarks>
    /// The path search list, which a process cannot start without. Picking a
    /// platform-specific name would make this test assert which machine it ran
    /// on rather than what the probe does.
    /// </remarks>
    private const string AlwaysSet = "PATH";

    [Fact]
    public void Read_ForAVariableTheSystemAlwaysSets_ReturnsIt() =>
        new ProcessEnvironmentProbe().Read(AlwaysSet).ShouldNotBeNullOrWhiteSpace();

    /// <remarks>
    /// A name in the shape of a unique identifier, so that "nothing sets this"
    /// is a property of the name rather than a hope about the machine.
    /// </remarks>
    [Fact]
    public void Read_ForANameNothingSets_ReturnsNull() =>
        new ProcessEnvironmentProbe().Read($"PREFLIGHT_{Guid.NewGuid():N}").ShouldBeNull();

    /// <summary>
    /// Two probes constructed at different moments answer from their own
    /// picture, not from a shared one.
    /// </summary>
    /// <remarks>
    /// The picture is taken per instance rather than once per process, which is
    /// what keeps a rule's view stable for the length of a run without making
    /// the whole tool remember an environment from whenever a type happened to
    /// load first.
    /// </remarks>
    [Fact]
    public void Read_FromTwoProbes_AgreesOnAVariableNeitherOfThemChanged() =>
        new ProcessEnvironmentProbe().Read(AlwaysSet)
            .ShouldBe(new ProcessEnvironmentProbe().Read(AlwaysSet));

    [Fact]
    public void Read_WithANullName_Throws() =>
        Should.Throw<ArgumentNullException>(() => new ProcessEnvironmentProbe().Read(null!));
}
