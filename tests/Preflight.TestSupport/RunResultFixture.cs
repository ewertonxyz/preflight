namespace Preflight.TestSupport;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core;
using Preflight.Core.Execution;

/// <summary>
/// Builds the runs the reporter and history tests render.
/// </summary>
/// <remarks>
/// <para>
/// Everything that varies between two runs of the same input is fixed here: the
/// <c>RunId</c> and every duration. The tool promises byte-identical output for
/// identical input, qualified for exactly those two, and a golden file cannot
/// exist until both are pinned.
/// </para>
/// <para>
/// It lives here, and not beside the reporters, because two projects need a
/// finished <c>RunResult</c>: the NDJSON record describes the same run the JSON
/// reporter does. Two fixtures claiming to be the canonical example would be
/// two examples, and the day they drifted apart neither golden file would say
/// so.
/// </para>
/// <para>
/// Still separate from <c>Core.Tests</c>' <c>RunFixture</c>. That one builds a
/// <c>RunRequest</c> to drive the executor; this builds a finished
/// <c>RunResult</c> to render, which is a different shape with different
/// interesting parts.
/// </para>
/// </remarks>
public static class RunResultFixture
{
    public static readonly Guid FixedRunId = new("11111111-2222-3333-4444-555555555555");

    public static readonly DateTimeOffset FixedStart =
        new(2026, 8, 26, 14, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// The canonical run: one pass, one failure with a full finding, and one
    /// skip attributed to the failure.
    /// </summary>
    public static RunResult DocumentedExample() => new()
    {
        RunId = FixedRunId,
        StartedAt = FixedStart,
        Duration = TimeSpan.FromSeconds(1),
        Stage = ValidationStage.BuildReadiness,
        Target = new BuildTarget("win64", "Development"),
        Pipeline = "atlas",
        PolicyChain = ["preflight.base.json", "preflight.atlas.json"],
        Verdict = RunVerdict.Blocked,
        Partial = false,
        FailOnWarning = false,
        NoSkip = false,
        Executions =
        [
            Execution("core.workspace.toolchain", RuleStatus.Passed, 0.4),
            Execution("core.build.configuration", RuleStatus.Failed, 0.6) with
            {
                Findings =
                [
                    new Finding
                    {
                        Message = "Missing platform configuration entry.",
                        Location = new FindingLocation("config/build/win64.json"),
                        Expected = "a \"contentRoot\" entry",
                        Actual = "key not present",
                        Remediation = "add \"contentRoot\" pointing to the packaged content folder",
                    },
                ],
            },
            Execution("core.build.compile-probe", RuleStatus.Skipped, 0) with
            {
                SkippedBecauseOf = [new RuleId("core.build.configuration")],
                SkipReason = SkipReason.DependencyFailed,
            },
        ],
    };

    public static RunResult With(params RuleExecution[] executions) =>
        DocumentedExample() with { Executions = executions };

    public static RuleExecution Execution(string id, RuleStatus status, double seconds) => new()
    {
        RuleId = new RuleId(id),
        Status = status,
        EffectiveSeverity = Severity.Error,
        Blocking = true,
        Gating = false,
        Duration = TimeSpan.FromSeconds(seconds),
    };
}
