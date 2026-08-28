namespace Preflight.Core.Policy;

/// <summary>
/// The outcome of resolving one policy file's <c>extends</c> chain: the merged
/// document, and the ordered list of files that produced it.
/// </summary>
/// <remarks>
/// <para>
/// The chain is a separate type rather than a member of
/// <see cref="PolicyDocument"/> on purpose. <c>PolicyDocument</c> is also the
/// type of the local overlay and of any single parsed file, and neither of
/// those has a chain of its own — a nullable member there would be null in most
/// of its uses, which is the shape that invites a null check at every call site
/// instead of at one.
/// </para>
/// <para>
/// Three separate consumers need this list and none of them can reconstruct it:
/// the console header prints <c>policy base → atlas</c>, the
/// <c>Policy chain</c> line of <c>explain</c> prints the same thing in full,
/// and <c>RunResult.PolicyChain</c> carries it into the history.
/// <see cref="PolicyDocument.FilePath"/> names only the entry file, and the
/// merged tree keeps per-leaf provenance but not the order the files were
/// applied in.
/// </para>
/// </remarks>
public sealed record PolicyLoadResult
{
    /// <summary>
    /// The fully merged policy, with the entry file's values taking precedence
    /// over its ancestors'.
    /// </summary>
    public required PolicyDocument Document { get; init; }

    /// <summary>
    /// The absolute paths of every file that contributed, in application order:
    /// the furthest ancestor first, the entry file last.
    /// </summary>
    /// <remarks>
    /// Always at least one element — the entry file itself. A file with no
    /// <c>extends</c> yields a chain of one, not an empty one.
    /// </remarks>
    public required IReadOnlyList<string> Chain { get; init; }

    /// <summary>
    /// Each file's own document, unmerged, in the same order as
    /// <see cref="Chain"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Document"/> cannot answer which file said what. Merging keeps
    /// per-leaf provenance, but a key that a descendant overrode leaves no
    /// trace of the ancestor's <em>declaration</em> — and for an array it
    /// leaves no trace at all, because <c>PolicyNode.Merge</c> replaces the
    /// stronger leaf whole.
    /// </para>
    /// <para>
    /// That is exactly what a seal is: a studio baseline declaring
    /// <c>sealed</c> would be erased by a pipeline document declaring its own,
    /// silently, and a baseline that quietly stops sealing is the governance
    /// false green the whole feature exists to remove. See ADR-031.
    /// </para>
    /// </remarks>
    public required IReadOnlyList<PolicyDocument> Documents { get; init; }
}
