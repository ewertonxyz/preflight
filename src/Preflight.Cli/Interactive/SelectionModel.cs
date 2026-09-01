namespace Preflight.Cli.Interactive;

/// <summary>
/// One row of a picker.
/// </summary>
/// <remarks>
/// <see cref="IsActive"/> and <see cref="IsAllowed"/> are two different facts
/// and neither implies the other. A version can be the one this machine is
/// pinned to and still be outside the range the checkout accepts, which is
/// exactly the state <c>pipeline use</c> exists to get somebody out of; showing
/// it as merely "current" would hide the reason they are looking at this list.
/// </remarks>
/// <param name="Label">What the person reads.</param>
/// <param name="Value">What choosing this row returns.</param>
/// <param name="IsActive">Whether this is what the machine uses today.</param>
/// <param name="IsAllowed">Whether choosing it would produce a workable state.</param>
public sealed record SelectionChoice(string Label, string Value, bool IsActive, bool IsAllowed);

/// <summary>
/// What a picker shows, as data.
/// </summary>
/// <remarks>
/// <para>
/// The model is computed here and rendered elsewhere, which is the arrangement
/// <c>HistoryReport</c> already uses: the report is a record and the renderer is
/// a separate type, so the numbers are assertable without a screen. The same
/// split is what makes this feature testable at all — the rendering belongs to
/// Spectre.Console and is deliberately not tested, because a test over it would
/// be a test of the library.
/// </para>
/// <para>
/// Order is ordinal and fixed here rather than left to whoever built the list.
/// A menu whose rows move between runs is a menu somebody chooses the wrong row
/// from. See ADR-035.
/// </para>
/// </remarks>
/// <param name="Choices">The rows, in the order they are shown.</param>
/// <param name="ActiveIndex">
/// Where the cursor starts: the active row, or <c>0</c> when none is active.
/// </param>
/// <param name="Prompt">The question, in one line.</param>
public sealed record SelectionModel(
    IReadOnlyList<SelectionChoice> Choices,
    int ActiveIndex,
    string Prompt)
{
    /// <summary>
    /// The pipelines installed on this machine, for <c>pipeline declare</c>.
    /// </summary>
    /// <param name="pipelines">Every installed pipeline name.</param>
    public static SelectionModel ForPipelines(IReadOnlyList<string> pipelines)
    {
        ArgumentNullException.ThrowIfNull(pipelines);

        return Build(
            [.. pipelines
                .Order(StringComparer.Ordinal)
                .Select(name => new SelectionChoice(name, name, IsActive: false, IsAllowed: true))],
            "Which pipeline is this checkout?");
    }

    /// <summary>
    /// The installed versions of one pipeline, for <c>pipeline use</c>.
    /// </summary>
    /// <remarks>
    /// <paramref name="requirement"/> decides <see cref="SelectionChoice.IsAllowed"/>
    /// and never removes a row. A version the checkout does not accept is
    /// still installed and still pinnable, and hiding it would leave somebody
    /// looking at a shorter list than their disk holds with nothing saying why.
    /// </remarks>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="versions">Every installed version of it.</param>
    /// <param name="pinned">What is pinned today, if anything.</param>
    /// <param name="requirement">The range the checkout accepts, if it states one.</param>
    public static SelectionModel ForVersions(
        string pipeline,
        IReadOnlyList<PackageVersion> versions,
        PackageVersion? pinned,
        PipelineRequirement? requirement)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(versions);

        return Build(
            [.. versions
                .OrderDescending()
                .Select(version => new SelectionChoice(
                    Label(version, pinned, requirement),
                    $"{pipeline}@{version}",
                    version == pinned,
                    requirement is null || version.Satisfies(requirement)))],
            $"Which version of '{pipeline}' should this machine use?");
    }

    private static SelectionModel Build(IReadOnlyList<SelectionChoice> choices, string prompt)
    {
        var active = -1;

        for (var index = 0; index < choices.Count; index++)
        {
            if (choices[index].IsActive)
            {
                active = index;

                break;
            }
        }

        return new SelectionModel(choices, active < 0 ? 0 : active, prompt);
    }

    private static string Label(
        PackageVersion version, PackageVersion? pinned, PipelineRequirement? requirement)
    {
        var notes = new List<string>(2);

        if (version == pinned)
        {
            notes.Add("pinned");
        }

        if (requirement is not null && !version.Satisfies(requirement))
        {
            notes.Add("outside the range this checkout accepts");
        }

        return notes.Count == 0
            ? version.ToString()
            : $"{version} ({string.Join(", ", notes)})";
    }
}
