namespace Preflight.Core.Policy;

/// <summary>
/// Where one effective policy value came from.
/// </summary>
/// <remarks>
/// <c>explain</c> shows three shapes: a file and line, a code default, and —
/// implicitly, since <c>--set</c> has neither — the command line.
/// <see cref="DescriptorDefault"/> and
/// <see cref="EngineDefault"/> are kept distinct even though both are "no file
/// touched this": one traces back to a specific <c>RuleDescriptor</c> field
/// (<c>DefaultSeverity</c>, <c>DefaultBlocking</c>, <c>DefaultGating</c>,
/// <c>DefaultTimeoutSeconds</c>), the other to a hardcoded engine constant that
/// isn't per-rule authored data (root keys, and the rule-level <c>enabled</c>
/// default, which has no corresponding descriptor field). Collapsing the two
/// would lose information a future <c>explain</c> cannot get back without
/// redoing the merge.
/// </remarks>
public abstract record PolicyOrigin
{
    private PolicyOrigin()
    {
    }

    public sealed record FromFile(string FilePath, int Line) : PolicyOrigin;

    public sealed record FromCommandLine : PolicyOrigin;

    /// <summary>
    /// A rule-scoped value that was never set for that rule, and so fell back
    /// to a root key — carrying which root key, and where <em>that</em> value
    /// in turn came from.
    /// </summary>
    /// <remarks>
    /// <c>explain</c> prints exactly this shape:
    /// <c>timeoutSeconds  60  preflight.base.json:5  (root defaultTimeoutSeconds)</c>.
    /// Flattening it to a plain <see cref="FromFile"/> would still name the
    /// right file and line, but would lose what makes that line readable — that
    /// the value is a root default, not something written about this rule. A
    /// reader sent to that line finds a key with a different name and concludes
    /// the report is wrong.
    /// </remarks>
    public sealed record FromRootKey(string RootKey, PolicyOrigin Source) : PolicyOrigin;

    /// <summary>
    /// A value written inside a <c>targets</c> block that matched this run.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="FromRootKey"/>, and for the same reason: <c>explain</c>
    /// prints <c>maxBytes  256  projectC.json:12  (target switch2)</c>, and
    /// flattening it to the file and line alone would keep the location while
    /// losing the one fact that explains why this run sees that number and
    /// another run does not.
    /// </remarks>
    public sealed record FromTarget(string TargetKey, PolicyOrigin Source) : PolicyOrigin;

    /// <summary>
    /// A value that came from a file inside an installed pipeline package.
    /// </summary>
    /// <remarks>
    /// A wrapper, like <see cref="FromRootKey"/> and <see cref="FromTarget"/>,
    /// and for the same reason: the inner origin still knows the file and the
    /// line, and this adds the one fact those cannot carry — which delivery of
    /// the pipeline that file arrived in. <c>explain</c> prints
    /// <c>maxBytes  256  projecta@1.4/acme.json:8</c>.
    /// <para>
    /// Without it, two runs of the same commit against two different packages
    /// produce identical output. The tool would then be claiming that the same
    /// inputs gave the same verdict when one of the inputs had changed, which
    /// is the one claim everything else here rests on.
    /// </para>
    /// </remarks>
    public sealed record FromPackage(string Pipeline, string Version, PolicyOrigin Source) : PolicyOrigin;

    public sealed record DescriptorDefault : PolicyOrigin;

    public sealed record EngineDefault : PolicyOrigin;
}
