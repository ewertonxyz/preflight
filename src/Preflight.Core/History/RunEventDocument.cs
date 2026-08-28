namespace Preflight.Core.History;

using System.Text.Json;
using System.Text.Json.Serialization;
using Preflight.Abstractions;

/// <summary>
/// The JSON shape of a run, used by both <c>--format json</c> and the NDJSON
/// history.
/// </summary>
/// <remarks>
/// <para>
/// One projection, not two. The console reporter and the history reporter
/// describing the same run in two hand-written shapes is the drift the history
/// cannot survive: a pipeline parses one of them today and a report reads the
/// other in thirty days, and nothing fails when they stop agreeing. The
/// serialiser options follow the same rule — the indented form is
/// <em>built from</em> the single-line form, so the only difference they can
/// ever have is the one that is written down.
/// </para>
/// <para>
/// Every enum is written as its name, never its ordinal. A consumer reading
/// <c>"verdict": 2</c> has to keep a copy of the enum's declaration order, and
/// inserting a value into that enum would silently change the meaning of every
/// record already written.
/// </para>
/// <para>
/// The order of <c>executions</c> is the order it was given. That order is
/// fixed, and the console spends it on the root cause being read before the
/// symptom; a serialiser that sorted by name for tidiness would take it back.
/// </para>
/// </remarks>
public static class RunEventDocument
{
    /// <summary>The <c>type</c> discriminator a run is written under.</summary>
    public const string EventType = "run";

    /// <summary>
    /// One record per line, for the history.
    /// </summary>
    public static JsonSerializerOptions SingleLine { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// The same document, indented, because the primary consumer of
    /// <c>--format json</c> is a person reading a CI log.
    /// </summary>
    public static JsonSerializerOptions Indented { get; } = new(SingleLine) { WriteIndented = true };

    /// <summary>
    /// The full record: every execution and every finding.
    /// </summary>
    public static object For(RunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new
        {
            type = EventType,
            runId = result.RunId,
            startedAt = result.StartedAt,
            durationMs = (long)result.Duration.TotalMilliseconds,
            stage = result.Stage,
            platform = result.Target.Platform,
            configuration = result.Target.Configuration,
            pipeline = result.Pipeline,
            pipelineVersion = result.PipelineVersion,
            policyChain = result.PolicyChain,
            verdict = result.Verdict,
            partial = result.Partial,
            failOnWarning = result.FailOnWarning,
            noSkip = result.NoSkip,

            // A run that executed nothing must not be indistinguishable
            // from an ordinary success to something reading this file. The console
            // says so in words; here it is a number a pipeline can branch on.
            executedCount = result.Executions.Count,
            executions = result.Executions.Select(Describe),
        };
    }

    /// <summary>
    /// The summary a record above the 64 KB line limit is replaced by.
    /// </summary>
    /// <remarks>
    /// A replacement, never a truncation of the full document's bytes. Cutting
    /// the tail off a UTF-8 string splits a code point and produces exactly the
    /// corrupt line the limit exists to prevent — and it would take the
    /// duration with it, which is the field the report depends on most. What is
    /// dropped is the finding detail, which is what filled the 64 KB in the
    /// first place; what survives is a count per rule, so the record still says
    /// which rule produced the flood.
    ///
    /// The pipeline and its version survive too, and were added when packages
    /// arrived. Provenance is the last thing worth dropping: the noisiest runs
    /// are exactly the ones somebody comes back to, and a record that cannot say
    /// which policy produced it is a record about nothing in particular. The
    /// policy chain still goes, because it is a list of paths rather than an
    /// identity. See ADR-034.
    /// </remarks>
    public static object Truncated(RunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new
        {
            type = EventType,
            runId = result.RunId,
            startedAt = result.StartedAt,
            durationMs = (long)result.Duration.TotalMilliseconds,
            stage = result.Stage,
            pipeline = result.Pipeline,
            pipelineVersion = result.PipelineVersion,
            verdict = result.Verdict,
            partial = result.Partial,
            failOnWarning = result.FailOnWarning,
            noSkip = result.NoSkip,
            executedCount = result.Executions.Count,
            executions = result.Executions.Select(DescribeWithoutFindings),
            findingCounts = result.Executions
                .Where(execution => execution.Findings.Count > 0)
                .ToDictionary(
                    execution => execution.RuleId.Value,
                    execution => execution.Findings.Count,
                    StringComparer.Ordinal),
            truncated = true,
        };
    }

    private static object Describe(RuleExecution execution) => new
    {
        ruleId = execution.RuleId.Value,
        status = execution.Status,

        // Recorded, not consulted: a report over thirty days has to
        // answer "was this rule blocking when it failed?" after the policy has
        // changed, and instrumentation that omits the policy in force produces
        // numbers that look historical and are not.
        effectiveSeverity = execution.EffectiveSeverity,
        blocking = execution.Blocking,
        gating = execution.Gating,
        durationMs = (long)execution.Duration.TotalMilliseconds,
        fromCache = execution.FromCache,
        skipReason = execution.SkipReason,
        skippedBecauseOf = execution.SkippedBecauseOf.Count == 0
            ? null
            : execution.SkippedBecauseOf.Select(id => id.Value),
        errorDetail = execution.ErrorDetail,
        findings = execution.Findings.Count == 0 ? null : execution.Findings.Select(Describe),
    };

    /// <remarks>
    /// <c>fromCache</c> survives truncation while the finding detail does not,
    /// because the report needs it to decide whether the duration beside it is
    /// a run or a lookup. A record that dropped it would contribute a
    /// nought-second execution to the "slowest rules" ranking with nothing to
    /// mark it.
    /// </remarks>
    private static object DescribeWithoutFindings(RuleExecution execution) => new
    {
        ruleId = execution.RuleId.Value,
        status = execution.Status,
        durationMs = (long)execution.Duration.TotalMilliseconds,
        fromCache = execution.FromCache,
    };

    private static object Describe(Finding finding) => new
    {
        message = finding.Message,
        path = finding.Location?.RelativePath,
        line = finding.Location?.Line,
        column = finding.Location?.Column,
        expected = finding.Expected,
        actual = finding.Actual,
        remediation = finding.Remediation,
    };
}
