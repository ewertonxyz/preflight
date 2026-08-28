namespace Preflight.Cli.Reporting;

using System.Text.Json;
using Preflight.Core;
using Preflight.Core.History;

/// <summary>
/// Renders a run as a single JSON document, for <c>--format json</c>.
/// </summary>
/// <remarks>
/// The document itself is <see cref="RunEventDocument"/>, which the NDJSON
/// history also writes. That was a promise in this file's remarks long before
/// there was a second writer: the thing a pipeline parses and the thing the
/// history holds are the same shape rather than two that drift — and nothing
/// defended it until there were two writers to defend it against.
/// </remarks>
public sealed class JsonReporter
{
    private readonly TextWriter _output;

    public JsonReporter(TextWriter output)
    {
        _output = output;
    }

    public void Report(RunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _output.Write(JsonSerializer.Serialize(RunEventDocument.For(result), RunEventDocument.Indented));
        _output.Write('\n');
    }
}
