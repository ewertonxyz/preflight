namespace Preflight.Cli.Commands;

using Preflight.Abstractions.Rules;
using Preflight.Core;
using Preflight.Core.Plugins;
using Preflight.Core.Policy;

/// <summary>
/// Raised when a pipeline source tree does not hold together.
/// </summary>
/// <remarks>
/// Carries every problem, not the first one. A configuration error, so it exits
/// 2 through the one mapping that decides exit codes: a tree that does not hold
/// together is the author's file, not a defect in this tool.
/// </remarks>
public sealed class PipelineValidationException : ConfigurationLoadException
{
    public PipelineValidationException(IReadOnlyList<string> problems)
        : base(string.Join(Environment.NewLine, problems)) => Problems = problems;

    /// <summary>Everything wrong with the tree, found in one pass.</summary>
    public IReadOnlyList<string> Problems { get; }
}
