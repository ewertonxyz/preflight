namespace Preflight.Abstractions.Rules;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Services;

/// <summary>
/// Everything a rule receives from the tool to do its work.
/// </summary>
/// <remarks>
/// <para>
/// Four services every rule can count on, plus two a host may or may not
/// offer, and deliberately no <see cref="IChangeSource"/> among them: it
/// populates <see cref="ChangedFiles"/> for the tool, it is never delivered to
/// the rule itself.
/// </para>
/// <para>
/// <see cref="Volumes"/> and <see cref="Environment"/> are the two that are not
/// required. Making either required would break every plugin author who
/// constructs a context in their own unit tests, to serve a capability one rule
/// needs — so the rule that finds one absent reports that it checked nothing
/// instead. The remarks on each say why <em>that</em> one is optional, because
/// the arguments are not the same and a third optional member must not arrive
/// simply because two already exist.
/// </para>
/// </remarks>
public sealed class RuleContext
{
    public required DirectoryInfo WorkspaceRoot { get; init; }

    public required ValidationStage Stage { get; init; }

    public required BuildTarget Target { get; init; }

    public required IReadOnlyList<ChangedFile> ChangedFiles { get; init; }

    public required IPolicyReader Policy { get; init; }

    public required IRuleLogger Logger { get; init; }

    public required IFileSystem FileSystem { get; init; }

    public required IProcessRunner Processes { get; init; }

    /// <summary>
    /// Reads free space on the volumes the workspace declares, when the host
    /// offers one.
    /// </summary>
    /// <remarks>
    /// Optional and nullable on purpose. A rule author constructs a context in
    /// their own unit tests, and a required member added here would break every
    /// one of those while the interface it carries is needed by a single rule.
    /// A rule that finds it absent reports that it checked nothing.
    /// </remarks>
    public IVolumeProbe? Volumes { get; init; }

    /// <summary>
    /// Reads the environment the tool was started with, when the host offers
    /// one.
    /// </summary>
    /// <remarks>
    /// Optional and nullable, the second member of this type that is not
    /// required, and for the same reason as the first: a rule author constructs
    /// a context in their own unit tests, and a required member added here
    /// would break every one of those to serve a capability one rule needs. A
    /// rule that finds it absent reports that it checked nothing.
    /// </remarks>
    public IEnvironmentProbe? Environment { get; init; }
}
