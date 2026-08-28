namespace Preflight.Core.Tests.Execution;

using System.Collections.Concurrent;
using Preflight.Abstractions;
using Preflight.Core;

/// <summary>
/// Captures what each rule logged, and which rule logged it.
/// </summary>
/// <remarks>
/// The queue is concurrent because rules in one level write at the same time —
/// serialising those writes is the engine's job,
/// and a fixture that could not survive the concurrency would be unable to
/// observe whether the engine did it.
/// </remarks>
internal sealed class RecordingRuleLoggerFactory : IRuleLoggerFactory
{
    public ConcurrentQueue<(RuleId RuleId, string Level, string Message)> Entries { get; } = new();

    public IRuleLogger ForRule(RuleId ruleId) => new RecordingRuleLogger(this, ruleId);

    public IReadOnlyList<string> MessagesFor(RuleId ruleId) =>
        [.. Entries.Where(entry => entry.RuleId == ruleId).Select(entry => entry.Message)];

    private sealed class RecordingRuleLogger : IRuleLogger
    {
        private readonly RecordingRuleLoggerFactory _factory;
        private readonly RuleId _ruleId;

        public RecordingRuleLogger(RecordingRuleLoggerFactory factory, RuleId ruleId)
        {
            _factory = factory;
            _ruleId = ruleId;
        }

        public void Debug(string message) => _factory.Entries.Enqueue((_ruleId, nameof(Debug), message));

        public void Info(string message) => _factory.Entries.Enqueue((_ruleId, nameof(Info), message));

        public void Warn(string message) => _factory.Entries.Enqueue((_ruleId, nameof(Warn), message));
    }
}
