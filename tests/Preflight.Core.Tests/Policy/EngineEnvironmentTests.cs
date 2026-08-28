namespace Preflight.Core.Tests.Policy;

using Preflight.Abstractions;
using Preflight.Core.Policy;

/// <summary>
/// Fixes the one engine default that is derived from the machine rather than
/// written down.
/// </summary>
/// <remarks>
/// policy precedence lists <c>maxDegreeOfParallelism</c> as a root key
/// whose engine default is the processor count, and the explain command prints
/// effective values with their origin. Without a seam, any golden file covering
/// that output asserts the core count of whoever recorded it.
/// </remarks>
public sealed class EngineEnvironmentTests
{
    private static readonly EngineEnvironment SevenCores = new()
    {
        ProcessorCount = 7,
        MachineName = "WKS-1234",
        ProcessId = 4242,
    };

    private static readonly RuleDescriptor LargeFile = new()
    {
        Id = new RuleId("core.presubmit.large-file"),
        DisplayName = "Large changed file",
        Stage = ValidationStage.PreSubmit,
    };

    [Fact]
    public void Build_WithAnInjectedEnvironment_SeedsMaxDegreeOfParallelismFromIt()
    {
        var policy = EffectivePolicy.Build(
            [LargeFile],
            pipeline: null,
            local: null,
            setOverrides: [],
        target: StatedBuildTarget.Unstated,
            SevenCores);

        policy.RootValue<long>("maxDegreeOfParallelism").Value.ShouldBe(7L);
    }

    /// <remarks>
    /// The injected value is a default, not an override: it still sits at the
    /// weakest layer, so a policy file continues to win over it. A seam that
    /// accidentally promoted the value would pass the test above and break the
    /// precedence table of policy precedence.
    /// </remarks>
    [Fact]
    public void Build_WithAnInjectedEnvironment_IsStillOverriddenByThePolicyFile()
    {
        var policy = EffectivePolicy.Build(
            [LargeFile],
            PolicyDocument.Parse(
                """{ "schemaVersion": 1, "maxDegreeOfParallelism": 2 }""",
                "preflight.base.json"),
            local: null,
            setOverrides: [],
        target: StatedBuildTarget.Unstated,
            SevenCores);

        policy.RootValue<long>("maxDegreeOfParallelism").Value.ShouldBe(2L);
        policy.RootValue<long>("maxDegreeOfParallelism").Origin.ShouldBeOfType<PolicyOrigin.FromFile>();
    }

    /// <remarks>
    /// Omitting the argument reads the real machine, which is what every
    /// production call site does. Asserted against
    /// <see cref="Environment.ProcessorCount"/> rather than a literal, because a
    /// literal here would be the machine-dependent assertion this whole seam
    /// exists to eliminate.
    /// </remarks>
    [Fact]
    public void Build_WithoutAnEnvironment_FallsBackToTheRealMachine()
    {
        var policy = EffectivePolicy.Build([LargeFile], pipeline: null, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

        policy.RootValue<long>("maxDegreeOfParallelism").Value.ShouldBe((long)Environment.ProcessorCount);
    }

    /// <remarks>
    /// Asserted against <see cref="Environment"/> rather than against literals,
    /// because a literal here would be the machine-dependent assertion this
    /// whole seam exists to eliminate. The machine name and the process id
    /// joined the record in the history: the history format names a history file after
    /// both, and reading them straight from the environment would make the file
    /// name untestable in exactly the way the processor count already was.
    /// </remarks>
    [Fact]
    public void Current_ReportsTheRealMachine()
    {
        EngineEnvironment.Current.ProcessorCount.ShouldBe(Environment.ProcessorCount);
        EngineEnvironment.Current.MachineName.ShouldBe(Environment.MachineName);
        EngineEnvironment.Current.ProcessId.ShouldBe(Environment.ProcessId);
    }
}
