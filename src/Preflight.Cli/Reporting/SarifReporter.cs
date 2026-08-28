namespace Preflight.Cli.Reporting;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core;
using Preflight.Core.History;

/// <summary>
/// Renders a run as a SARIF 2.1.0 document, for <c>--format sarif</c>.
/// </summary>
/// <remarks>
/// <para>
/// The point of this format is that the code review pipeline already reads it,
/// so Preflight does not have to be integrated to be consumed. Determinism
/// applies here as it does to the other two reporters: two identical runs
/// differ only in the run id and the two invocation timestamps — a narrower
/// variation than the other two have, because this document carries no
/// durations at all.
/// </para>
/// <para>
/// The serialiser options are <see cref="RunEventDocument"/>'s, so that the two
/// machine-readable documents this tool emits cannot drift on casing or on how
/// a null is treated. What they do not share is the shape: SARIF is somebody
/// else's schema, and this file follows it rather than the run event.
/// </para>
/// <para>
/// The decision worth reading first: an <c>Errored</c> rule produces no result.
/// </para>
/// </remarks>
public sealed class SarifReporter
{
    private const string SchemaUri = "https://json.schemastore.org/sarif-2.1.0.json";

    private const string SarifVersion = "2.1.0";

    private const string DriverName = "Preflight";

    private readonly TextWriter _output;

    public SarifReporter(TextWriter output)
    {
        _output = output;
    }

    /// <summary>
    /// Writes the whole document.
    /// </summary>
    /// <param name="result">The finished run.</param>
    /// <param name="descriptors">
    /// The discovered rules, which is where <c>DisplayName</c> and
    /// <c>Documentation</c> come from, since both live off
    /// <c>RuleExecution</c>.
    /// </param>
    public void Report(RunResult result, IReadOnlyList<RuleDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(descriptors);

        var byId = descriptors
            .GroupBy(descriptor => descriptor.Id)
            .ToDictionary(group => group.Key, group => group.First());

        var document = new Document { Runs = [Run(result, byId)] };

        _output.Write(JsonSerializer.Serialize(document, RunEventDocument.Indented));
        _output.Write('\n');
    }

    /// <summary>
    /// The document root, which is a declared type rather than an anonymous one
    /// for exactly one reason: <c>$schema</c> is not a legal C# identifier.
    /// </summary>
    private sealed record Document
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; init; } = SchemaUri;

        public string Version { get; init; } = SarifVersion;

        public required object[] Runs { get; init; }
    }

    private static object Run(RunResult result, IReadOnlyDictionary<RuleId, RuleDescriptor> byId) => new
    {
        tool = new
        {
            driver = new
            {
                name = DriverName,

                // One entry per execution, in the presentation order of section
                // 8.3, so that ruleIndex means the same thing in two runs of the
                // same workspace. Emitting only the rules that produced a
                // finding would make the index depend on the outcome.
                //
                // No driver version: it would change the bytes of this document
                // on every release, for a field nothing here needs.
                rules = result.Executions.Select(execution => Rule(execution, byId)),
            },
        },
        invocations = new[] { Invocation(result) },

        // The run id, and — with the two timestamps above — the whole
        // of what may differ between two runs of the same workspace.
        automationDetails = new { guid = result.RunId },

        // An array, always, even when it is empty: to a parser an absent array
        // and an empty one are different facts.
        results = result.Executions
            .Select((execution, index) => (execution, index))
            .Where(pair => pair.execution.Status != RuleStatus.Errored)
            .SelectMany(pair => Results(pair.execution, pair.index)),
    };

    /// <remarks>
    /// One result per finding, because a result is what a code review renders
    /// as a single annotation and a finding is one piece of evidence.
    /// <paramref name="index"/> is the execution's own position, which is also
    /// its driver rule's, since both lists are built from <c>Executions</c> in
    /// the same order.
    /// </remarks>
    private static IEnumerable<object> Results(RuleExecution execution, int index)
    {
        // A rule with no findings still produces one result. Dropping it would
        // leave a consumer unable to tell a rule that passed from a rule that
        // never ran, and the exit codes spend a whole table on that difference.
        if (execution.Findings.Count == 0)
        {
            return [Result(execution, index, Summarise(execution), location: null)];
        }

        return execution.Findings.Select(finding =>
            Result(execution, index, Describe(finding), finding.Location));
    }

    private static object Result(RuleExecution execution, int index, string message, FindingLocation? location) => new
    {
        ruleId = execution.RuleId.Value,
        ruleIndex = index,
        kind = SarifMapping.KindOf(execution.Status),
        level = SarifMapping.LevelOf(execution.Status, execution.EffectiveSeverity),
        message = new { text = message },
        locations = location is null ? null : new[] { Location(location) },
    };

    private static object Location(FindingLocation location)
    {
        object? region = location.Line is null
            ? null
            : new
            {
                startLine = location.Line,
                startColumn = location.Column,
            };

        return new
        {
            physicalLocation = new
            {
                // Forward slashes whatever the platform produced. A SARIF uri is
                // a URI reference, and a Windows path with backslashes in it is
                // not one — a consumer would fail to match it against a file in
                // the repository and drop the annotation without saying so.
                artifactLocation = new { uri = location.RelativePath.Replace('\\', '/') },
                region,
            },
        };
    }

    /// <remarks>
    /// <para>
    /// The console's order — the message, then what was expected, then what was
    /// there, then how to fix it — folded into the one field every SARIF
    /// consumer displays. Each part is emitted only when it has something to
    /// say, exactly as the console reporter does: every member below
    /// <c>Message</c> is independently optional, and a row of empty labels is a
    /// template pretending to be evidence.
    /// </para>
    /// <para>
    /// Not <c>fixes[]</c>: that field carries <c>artifactChanges</c>, an
    /// applicable correction, and writing to the workspace is a non-goal.
    /// Offering the field would promise what the tool refuses to do. Not
    /// <c>properties</c> either, since no consumer renders the property bag.
    /// </para>
    /// </remarks>
    private static string Describe(Finding finding)
    {
        var writer = new StringBuilder(finding.Message);

        AppendLabelled(writer, "expected", finding.Expected);
        AppendLabelled(writer, "actual", finding.Actual);

        // 'fix' rather than 'remediation': the console chose the shorter word,
        // and the console report already reads that way.
        AppendLabelled(writer, "fix", finding.Remediation);

        return writer.ToString();
    }

    private static void AppendLabelled(StringBuilder writer, string label, string? value)
    {
        if (value is not null)
        {
            writer.Append('\n').Append(label).Append(": ").Append(value);
        }
    }

    /// <remarks>
    /// What a result says when the rule produced no finding of its own. The
    /// skip attribution is carried through because those causes are ordered by
    /// topological level so the likeliest root reads first, and that whole
    /// ordering is spent on the root cause being visible before the symptom.
    /// </remarks>
    private static string Summarise(RuleExecution execution)
    {
        var sentence = execution.Status switch
        {
            RuleStatus.Passed => "The rule passed.",
            RuleStatus.Warning => "The rule reported a warning.",
            RuleStatus.Failed => "The rule failed.",
            RuleStatus.NotApplicable => "The rule had nothing to check.",
            _ => "The rule was skipped.",
        };

        return execution.SkippedBecauseOf.Count == 0
            ? sentence
            : sentence + " Blocked by " +
                string.Join(", ", execution.SkippedBecauseOf.Select(id => id.Value)) + ".";
    }

    private static object Rule(RuleExecution execution, IReadOnlyDictionary<RuleId, RuleDescriptor> byId)
    {
        // A run only ever reports on rules it discovered, so a miss is reachable
        // only through this class's own public surface. It writes the id alone
        // rather than inventing a display name: a fabricated name on a code
        // review screen is worse than a missing one.
        var descriptor = byId.GetValueOrDefault(execution.RuleId);

        object? shortDescription = descriptor is null ? null : new { text = descriptor.DisplayName };

        return new
        {
            id = execution.RuleId.Value,
            name = descriptor?.DisplayName,
            shortDescription,

            // Absent rather than empty when the rule has no documentation:
            // it is nullable, and a link to nowhere is worse than
            // no link.
            helpUri = descriptor?.Documentation,
        };
    }

    /// <remarks>
    /// <para>
    /// Where <c>--fail-on-warning</c> shows up, and the only place it does. The
    /// promotion is already applied to <c>Verdict</c> by the executor, so the
    /// exit code follows from the verdict alone; recomputing it here would put
    /// a second copy of the promotion rule in the reporter, and the two would
    /// diverge the day the rule changed. Rewriting the <c>level</c> instead
    /// would make the same findings produce two different documents according
    /// to an invocation flag.
    /// </para>
    /// <para>
    /// An <c>Errored</c> rule arrives here as a notification and nowhere else.
    /// The visible cost is real and worth stating: a consumer that reads only
    /// <c>results</c> sees a clean run. The antidote is the exit code, which
    /// the exit code already calls the tool's owner rather than the author of
    /// the commit — not moving the defect into the results, which would do the
    /// opposite.
    /// </para>
    /// </remarks>
    private static object Invocation(RunResult result)
    {
        var exitCode = ExitCode.ForVerdict(result.Verdict);

        var notifications = result.Executions
            .Where(execution => execution.Status == RuleStatus.Errored)
            .Select(Notification)
            .ToArray();

        return new
        {
            executionSuccessful = exitCode == ExitCode.Success,
            exitCode,
            startTimeUtc = Timestamp(result.StartedAt),
            endTimeUtc = Timestamp(result.StartedAt + result.Duration),
            toolExecutionNotifications = notifications.Length == 0 ? null : notifications,
        };
    }

    private static object Notification(RuleExecution execution) => new
    {
        level = "error",
        message = new
        {
            text = execution.ErrorDetail ?? "The rule errored without reporting a detail.",
        },
        associatedRule = new { id = execution.RuleId.Value },
    };

    /// <remarks>
    /// The SARIF date-time format, in UTC, through
    /// <see cref="CultureInfo.InvariantCulture"/>. The author's machine is
    /// pt-BR and CI is almost certainly en-US; with the ambient culture,
    /// the byte-identical guarantee would hold on each machine
    /// separately and fail between them.
    /// </remarks>
    private static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}
