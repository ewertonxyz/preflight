namespace Preflight.Rules;

using System.Text.RegularExpressions;

/// <summary>
/// One <c>where to look, and what to capture</c> entry, parsed from policy.
/// </summary>
/// <remarks>
/// <para>
/// A string with an arrow rather than an object with two fields, in the shape
/// the companion pairs already use: policy settings carry homogeneous arrays of
/// scalars, and an object inside an array would need the parser, the policy node
/// and the scoped reader all to learn a shape none of them has.
/// </para>
/// <para>
/// Parsed and compiled once, before any file is opened, so that a typo is
/// reported once against the policy rather than once per file that happened to
/// match nothing.
/// </para>
/// </remarks>
internal sealed record PinnedReference
{
    /// <summary>
    /// What separates the path pattern from the expression.
    /// </summary>
    public const string Arrow = "->";

    private PinnedReference(string text, GlobPattern when, Regex expression)
    {
        Text = text;
        When = when;
        Expression = expression;
    }

    /// <summary>
    /// The entry as the policy wrote it, for a report that has to name it.
    /// </summary>
    public string Text { get; }

    /// <summary>The paths this entry applies to.</summary>
    public GlobPattern When { get; }

    /// <summary>The expression whose single group is the reference.</summary>
    public Regex Expression { get; }

    /// <summary>
    /// Reads one entry.
    /// </summary>
    /// <param name="entry">The entry as the policy wrote it.</param>
    /// <returns>
    /// The reference, or <see langword="null"/> when the entry has no arrow,
    /// either side of it is empty, the expression will not compile, or it does
    /// not have exactly one capturing group.
    /// </returns>
    /// <remarks>
    /// Null rather than an exception, because a policy typo is something the
    /// reader has to be told about in a finding that names the entry — not an
    /// errored rule that names a stack.
    ///
    /// Split at the <em>first</em> arrow. A path pattern never legitimately
    /// contains one and an expression frequently does, so splitting at the last
    /// would break the entry that matches an arrow in the text it is looking at.
    /// </remarks>
    public static PinnedReference? Parse(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var arrow = entry.IndexOf(Arrow, StringComparison.Ordinal);

        if (arrow < 0)
        {
            return null;
        }

        var pattern = entry[..arrow].Trim();
        var expression = entry[(arrow + Arrow.Length)..].Trim();

        if (pattern.Length == 0 || expression.Length == 0 || Compile(expression) is not { } compiled)
        {
            return null;
        }

        // Group zero is the whole match and is always there, so exactly one
        // capture is two numbers. Counted through the expression rather than by
        // looking for parentheses in the text, which gets it wrong in both
        // directions and gets it wrong silently: a non-capturing group looks
        // like a capture, and an escaped bracket does not look like anything.
        return compiled.GetGroupNumbers().Length == 2
            ? new PinnedReference(entry, GlobPattern.Compile(pattern), compiled)
            : null;
    }

    /// <summary>
    /// Compiles one expression from policy, or answers null if it will not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matched without backtracking, which is linear by construction. It removes
    /// the failure this whole rule would otherwise invite: an expression written
    /// in policy that takes exponential time on some file nobody anticipated,
    /// reported as a rule that timed out somewhere far from the entry that
    /// caused it. There is no match deadline here and no arm to catch one,
    /// because neither can happen.
    /// </para>
    /// <para>
    /// What it costs is written down rather than discovered: matching that way
    /// refuses backreferences and lookaround, so an entry using either does not
    /// compile and lands in the same "malformed entry" finding as a missing
    /// bracket — which is a far better message than an expired deadline, and one
    /// the reader can act on.
    /// </para>
    /// <para>
    /// Culture-invariant, and nothing else. Explicit capture would zero the
    /// group count every entry is validated against; multiline is something an
    /// entry writes for itself when it wants it; and emitting IL for a handful
    /// of short expressions in a process that lives for seconds costs more than
    /// interpreting them.
    /// </para>
    /// </remarks>
    public static Regex? Compile(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        try
        {
            return new Regex(
                expression,
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            // Two ways an entry can fail to compile, and both are the policy's
            // mistake rather than the tool's. A missing bracket is an argument
            // the runtime refuses; a backreference or a lookaround is a
            // construct linear matching declines to support, and it says so
            // with a different exception. Catching only the first would turn
            // the second into an errored rule, which accuses the tool of a
            // defect for a line somebody wrote.
            return null;
        }
    }
}
