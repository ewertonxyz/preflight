namespace Preflight.Cli.Pipelines;

/// <summary>
/// Why this run is using the pipeline version it is using.
/// </summary>
/// <remarks>
/// <para>
/// Lives here rather than on <c>RunResult</c>, which is in
/// <c>Preflight.Core</c>. That is the arrangement <see cref="PipelineSource"/>
/// already has, and for the same reason: the version itself is a fact a machine
/// reader needs in order to tell two runs of one commit apart, so it travels on
/// the result; <em>why</em> that version was chosen is an explanation for the
/// person reading the header, and it travels beside the report the way the
/// selection source does. A machine reader gets the fact; a person gets the
/// explanation; and neither has to parse the other's form to find it.
/// </para>
/// </remarks>
public enum PipelineVersionSource
{
    /// <summary>No package took part.</summary>
    /// <remarks>
    /// Covers both worlds in which there is nothing to say: a workspace holding
    /// its own <c>preflight.&lt;name&gt;.json</c>, and a workspace that selected
    /// no pipeline at all. Neither reaches the console header, because in
    /// neither case is there a version to name — which is what keeps the report
    /// of a run that never met a package byte-identical to the one this tool
    /// printed before packages existed.
    /// </remarks>
    None,

    /// <summary>The machine pins this version.</summary>
    Pin,

    /// <summary>No pin; the newest installed version the checkout's range accepts.</summary>
    Requirement,

    /// <summary>No pin and no range; the newest installed version.</summary>
    Newest,
}
