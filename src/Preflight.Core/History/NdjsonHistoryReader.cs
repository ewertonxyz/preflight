namespace Preflight.Core.History;

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Services;
using Preflight.Core.Execution;

/// <summary>
/// Reads the history back, one line at a time.
/// </summary>
/// <remarks>
/// <para>
/// Streaming rather than materialised, and that is a decision taken on the
/// first line of code rather than deferred. Streaming aggregation is the first
/// remedy reached for when a history outgrows the month it was written in, and
/// it is cheap while the signature yields — a signature returning a list is
/// what makes it stop being cheap, because by then every caller has indexed
/// into one.
/// </para>
/// <para>
/// Reading needs no interface of its own: <see cref="IFileSystem"/> already
/// exposes exactly the three members this wants, and adding a fourth to a
/// contract plugins compile against would oblige every one of them to be
/// rebuilt. Writing is the direction that needed a new interface.
/// </para>
/// <para>
/// Files are visited in ordinal order of their full path. <c>EnumerateFiles</c>
/// promises no order at all, and the file system must not be allowed to decide
/// anything a report prints.
/// </para>
/// </remarks>
public sealed class NdjsonHistoryReader
{
    private readonly IFileSystem _fileSystem;

    public NdjsonHistoryReader(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Every line in <paramref name="directory"/>, in a fixed order.
    /// </summary>
    /// <remarks>
    /// A directory that is not there yields nothing. It is created on the first
    /// write, so its absence is the ordinary state of a workspace that has not
    /// been validated yet — and an empty history is a valid answer rather than
    /// an error.
    /// </remarks>
    public async IAsyncEnumerable<HistoryEntry> ReadAsync(
        string directory,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_fileSystem.DirectoryExists(directory))
        {
            yield break;
        }

        var files = _fileSystem
            .EnumerateFiles(directory, HistoryPaths.SearchPattern, SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal);

        foreach (var file in files)
        {
            await using var stream = _fileSystem.OpenRead(file);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var number = 0;

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                number++;

                // Every append-only file ends in a terminator, so the last read
                // of every real history file is an empty line. Counting it as
                // damage would report corruption in a file nothing damaged.
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                yield return Parse(line, file, number);
            }
        }
    }

    private static HistoryEntry Parse(string line, string file, int number)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            // An interleaved line, from a concurrent append. Not an error:
            // a fact the report has to be able to state a count of.
            return new HistoryEntry.Unreadable(file, number);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new HistoryEntry.Unreadable(file, number);
            }

            return Text(root, "type") switch
            {
                RunEventDocument.EventType => ReadRun(root, file, number),
                ExternalEventDocument.EventType => ReadExternal(root, file, number),
                { } type => new HistoryEntry.Ignored(type),
                null => new HistoryEntry.Unreadable(file, number),
            };
        }
    }

    private static HistoryEntry ReadRun(JsonElement root, string file, int number)
    {
        if (Instant(root) is not { } startedAt ||
            Milliseconds(root) is not { } duration ||
            Enumeration<ValidationStage>(root, "stage") is not { } stage ||
            Enumeration<RunVerdict>(root, "verdict") is not { } verdict ||
            Executions(root) is not { } executions)
        {
            return new HistoryEntry.Unreadable(file, number);
        }

        return new HistoryEntry.Parsed(new HistoryEvent.Run
        {
            StartedAt = startedAt,
            Duration = duration,
            Stage = stage,
            Verdict = verdict,
            Partial = Flag(root, "partial"),
            FailOnWarning = Flag(root, "failOnWarning"),
            NoSkip = Flag(root, "noSkip"),
            ExecutedCount = (int)(Integer(root, "executedCount") ?? executions.Count),
            Executions = executions,
        });
    }

    private static HistoryEntry ReadExternal(JsonElement root, string file, int number)
    {
        if (Instant(root) is not { } startedAt ||
            Milliseconds(root) is not { } duration ||
            Text(root, "label") is not { } label ||
            Integer(root, "exitCode") is not { } exitCode)
        {
            return new HistoryEntry.Unreadable(file, number);
        }

        return new HistoryEntry.Parsed(new HistoryEvent.External
        {
            StartedAt = startedAt,
            Duration = duration,
            Label = label,
            ExitCode = (int)exitCode,
        });
    }

    /// <summary>
    /// The executions of a run, or <see langword="null"/> when one of them is
    /// malformed.
    /// </summary>
    /// <remarks>
    /// A truncated record legitimately carries fewer fields per execution, but
    /// it still carries the three this reads. An absent <c>executions</c> array
    /// is accepted as empty; a present one that does not parse makes the whole
    /// line unreadable, because a partially understood run would contribute a
    /// duration to the percentiles and a wrong count to "slowest rules" at the
    /// same time.
    /// </remarks>
    private static List<HistoryExecution>? Executions(JsonElement root)
    {
        if (!root.TryGetProperty("executions", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var executions = new List<HistoryExecution>(array.GetArrayLength());

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object ||
                Text(element, "ruleId") is not { } ruleId ||
                Enumeration<RuleStatus>(element, "status") is not { } status ||
                Milliseconds(element) is not { } duration)
            {
                return null;
            }

            executions.Add(new HistoryExecution(ruleId, status, duration, Flag(element, "fromCache")));
        }

        return executions;
    }

    private static DateTimeOffset? Instant(JsonElement element) =>
        Text(element, "startedAt") is { } text &&
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value
            : null;

    private static TimeSpan? Milliseconds(JsonElement element) =>
        Integer(element, "durationMs") is { } value ? TimeSpan.FromMilliseconds(value) : null;

    private static TEnum? Enumeration<TEnum>(JsonElement element, string name)
        where TEnum : struct, Enum =>
        Text(element, name) is { } text && Enum.TryParse<TEnum>(text, out var value) && Enum.IsDefined(value)
            ? value
            : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? Integer(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var number)
            ? number
            : null;

    /// <remarks>
    /// Absent reads as <see langword="false"/> rather than as damage. These
    /// three are the flags a run <em>was not</em> given, and a writer that
    /// omits a false is still describing the same run.
    /// </remarks>
    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
