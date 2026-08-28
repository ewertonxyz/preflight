namespace Preflight.Core.Tests.History;

using Preflight.Core.History;
using Preflight.Core.Policy;

/// <summary>
/// Reading the two root keys of the history format out of a resolved policy.
/// </summary>
public sealed class HistorySettingsTests
{
    private static readonly EngineEnvironment Machine = new()
    {
        ProcessorCount = 8,
        MachineName = "WKS-1234",
        ProcessId = 4242,
    };

    [Fact]
    public void From_WithNothingConfigured_IsTheDocumentedDefaults()
    {
        var settings = HistorySettings.From(PolicyWith("""{ "schemaVersion": 1 }"""));

        settings.Path.ShouldBe(".preflight/history");
        settings.Mode.ShouldBe(HistoryMode.Shared);
    }

    [Fact]
    public void From_WithBothKeysSet_ReadsThem()
    {
        var settings = HistorySettings.From(PolicyWith("""
            {
              "schemaVersion": 1,
              "historyPath": "build/history",
              "historyMode": "per-process"
            }
            """));

        settings.Path.ShouldBe("build/history");
        settings.Mode.ShouldBe(HistoryMode.PerProcess);
    }

    /// <summary>
    /// A mode nobody can spell falls back to <c>shared</c> rather than throwing.
    /// </summary>
    /// <remarks>
    /// Only reachable by building a policy without validation, which is what
    /// this test does: <c>PolicyValidator</c> refuses the value at load time, so
    /// no real run gets here. Losing a run's record over a policy typo the
    /// validator has already reported would be instrumentation deciding it is
    /// more important than the run.
    /// </remarks>
    [Fact]
    public void From_WithAModeTheValidatorWouldHaveRefused_FallsBackToShared() =>
        HistorySettings.From(PolicyWith("""{ "schemaVersion": 1, "historyMode": "nonsense" }"""))
            .Mode.ShouldBe(HistoryMode.Shared);

    private static EffectivePolicy PolicyWith(string json) => EffectivePolicy.Build(
        [],
        PolicyDocument.Parse(json, "preflight.base.json"),
        local: null,
        setOverrides: [],
        target: StatedBuildTarget.Unstated,
        Machine);
}
