namespace Preflight.Cli.Reporting;

using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Core;
using Preflight.Core.Execution;

/// <summary>
/// The concrete log sink, writing to standard error.
/// </summary>
/// <remarks>
/// <para>
/// The one context service that stays in the CLI, because it is the only one
/// about presentation: <c>IRuleLoggerFactory</c>'s own remarks say the sink is
/// injected by the CLI so as to keep the engine from knowing how output is
/// formatted.
/// </para>
/// <para>
/// Standard error, not standard output. Under <c>--format json</c> the standard
/// output stream has to stay one parseable document, and a rule that logged a
/// line of progress would break the <c>jq</c> at the end of the pipeline. That
/// is not a rule author's problem to remember, so it is not left to them.
/// </para>
/// <para>
/// Writes are serialised, because the engine runs rules at the same level
/// concurrently — without the lock, two rules interleave mid-line and produce a
/// log that describes neither.
/// </para>
/// </remarks>
public sealed class ConsoleRuleLoggerFactory : IRuleLoggerFactory
{
    private readonly TextWriter _sink;
    private readonly Lock _gate = new();
    private readonly bool _verbose;

    /// <param name="sink">Where lines go. Standard error, in production.</param>
    /// <param name="verbose">
    /// Whether <c>Debug</c> is written. Off by default: a rule's debug output
    /// is for whoever is writing the rule, and the console spends its budget on
    /// the report.
    /// </param>
    public ConsoleRuleLoggerFactory(TextWriter sink, bool verbose = false)
    {
        _sink = sink;
        _verbose = verbose;
    }

    public IRuleLogger ForRule(RuleId ruleId) => new ScopedLogger(this, ruleId);

    private void Write(RuleId ruleId, string level, string message)
    {
        lock (_gate)
        {
            _sink.WriteLine($"{level,-5} {ruleId.Value}  {message}");
        }
    }

    /// <remarks>
    /// Scoped at construction rather than by the rule passing its own id. The
    /// id belongs on every line, and a logger that trusted the caller for it
    /// would put one rule's id on another rule's line the first time somebody
    /// copied a call.
    /// </remarks>
    private sealed class ScopedLogger : IRuleLogger
    {
        private readonly ConsoleRuleLoggerFactory _factory;
        private readonly RuleId _ruleId;

        public ScopedLogger(ConsoleRuleLoggerFactory factory, RuleId ruleId)
        {
            _factory = factory;
            _ruleId = ruleId;
        }

        public void Debug(string message)
        {
            if (_factory._verbose)
            {
                _factory.Write(_ruleId, "debug", message);
            }
        }

        public void Info(string message) => _factory.Write(_ruleId, "info", message);

        public void Warn(string message) => _factory.Write(_ruleId, "warn", message);
    }
}
