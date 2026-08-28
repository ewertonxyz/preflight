namespace Preflight.Abstractions.Services;

/// <summary>
/// The logger delivered to a rule, already scoped to the rule's id.
/// </summary>
/// <remarks>
/// Deliberately has no <c>Error</c> method: a rule reports a problem through a
/// finding, never through the log. A problem that only exists in the log is
/// invisible to the report, to SARIF and to the history — invisible to
/// everything that matters. Not offering the method is more effective than
/// documenting that it should not be used.
/// </remarks>
public interface IRuleLogger
{
    void Debug(string message);

    void Info(string message);

    void Warn(string message);
}
