namespace Preflight.Cli.Pipelines;

using Preflight.Abstractions.Services;
using Preflight.Core.Policy;

/// <summary>
/// Where the pipeline a run validates against was decided.
/// </summary>
/// <remarks>
/// Carried into the report because a run configured by a file nobody passed
/// must not be indistinguishable from one that was asked for. The local
/// overlay already makes that argument — the header says when it is in effect,
/// so a run that looks configured and is not cannot be mistaken for one that
/// is — and a pipeline selected off disk is the same class of fact.
/// </remarks>
public enum PipelineSource
{
    /// <summary>No pipeline: the chain starts at <c>preflight.base.json</c>, or nowhere.</summary>
    None,

    /// <summary><c>--pipeline</c>, or its deprecated spelling.</summary>
    CommandLine,

    /// <summary>The <c>pipeline</c> key in <c>preflight.base.json</c>.</summary>
    Checkout,
}
