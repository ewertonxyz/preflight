namespace Preflight.Rules;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Walks a change set on behalf of a pre-submit rule and turns what it found
/// into an outcome.
/// </summary>
/// <remarks>
/// <para>
/// It exists to keep three statements that must agree in one place. A deleted
/// file is not examined — deleting a forbidden file is the fix, not the
/// violation, and a deleted file has no size to measure, so asking for one
/// throws and the rule turns <c>Errored</c> on a perfectly ordinary commit. A
/// run that examined nothing reports <c>NotApplicable</c> rather than
/// <c>Passed</c>, because a tick would claim that files were measured and found
/// small when none were looked at. And a commit that only deletes things is
/// therefore <c>NotApplicable</c> too, which only holds if deletions are
/// filtered before the count rather than at the point of measurement.
/// </para>
/// <para>
/// Held by a rule, not inherited from. A base class would own
/// <c>ExecuteAsync</c> and hand the rules a template to fill in, which is a
/// larger promise than the two of them need: what they share is how a change
/// set is walked, and they disagree about almost everything else — one reads
/// the file system and the other never does, one takes its configuration as a
/// number and the other as a list of patterns.
/// </para>
/// </remarks>
internal sealed class ChangedFileScan
{
    private readonly List<Finding> _findings = [];

    private int _examined;

    /// <summary>
    /// Whether this file is one the rule should look at, counting it if so.
    /// </summary>
    public bool Examines(ChangedFile file)
    {
        if (file.Kind == ChangeKind.Deleted)
        {
            return false;
        }

        _examined++;

        return true;
    }

    /// <summary>
    /// Records one problem found on a file this scan examined.
    /// </summary>
    public void Report(Finding finding) => _findings.Add(finding);

    /// <summary>
    /// What the rule reports, given what the walk saw.
    /// </summary>
    public RuleOutcome Outcome() => _examined switch
    {
        0 => RuleOutcome.NotApplicable(),
        _ when _findings.Count > 0 => RuleOutcome.Failed([.. _findings]),
        _ => RuleOutcome.Passed(),
    };
}
