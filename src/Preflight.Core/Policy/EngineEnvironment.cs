namespace Preflight.Core.Policy;

/// <summary>
/// The facts about the machine that engine defaults and the history record are
/// derived from.
/// </summary>
/// <remarks>
/// <para>
/// One policy default is not a constant: <c>maxDegreeOfParallelism</c> starts
/// at the processor count. Read straight from <see cref="Environment"/>, it
/// makes the effective policy machine-dependent — and <c>explain</c> prints
/// effective policy, so a golden file containing the number passes on the
/// author's machine and fails on a colleague's, as an assertion about core
/// count wearing a policy report's clothes.
/// </para>
/// <para>
/// Redacting the value in the reporter was the alternative, and it is worse in
/// both directions. It hides a real effective value from the one command whose
/// entire purpose is showing where effective values come from, and it does not
/// scale: every future environment-derived default would need its own redaction
/// rule, in every reporter, forever. A seam here is one place.
/// </para>
/// <para>
/// A record rather than a bare <c>Func&lt;int&gt;</c> parameter for the same
/// reason. The next environment-derived default — available memory, an
/// architecture-dependent bound — is a property on this type, not a second
/// parameter on <c>Build</c> and a third after that.
/// </para>
/// <para>
/// A history file is named after the machine, and the <c>per-process</c> mode
/// after the process as well. Those are the same kind of fact as the processor
/// count — read once from the real machine, replaced wholesale by a test — so
/// they are properties here rather than a second machine-facts type whose only
/// difference would be which namespace it sits in. The remarks above
/// pre-authorised exactly this.
/// </para>
/// </remarks>
public sealed record EngineEnvironment
{
    /// <summary>
    /// The number of logical processors available to the engine. Seeds
    /// <c>maxDegreeOfParallelism</c>.
    /// </summary>
    public required int ProcessorCount { get; init; }

    /// <summary>
    /// The machine this run is happening on. Names the history file of section
    /// 10.1.
    /// </summary>
    public required string MachineName { get; init; }

    /// <summary>
    /// This process. Names the history file under
    /// <c>historyMode: per-process</c>.
    /// </summary>
    public required int ProcessId { get; init; }

    /// <summary>
    /// The real machine.
    /// </summary>
    public static EngineEnvironment Current => new()
    {
        ProcessorCount = Environment.ProcessorCount,
        MachineName = Environment.MachineName,
        ProcessId = Environment.ProcessId,
    };
}
