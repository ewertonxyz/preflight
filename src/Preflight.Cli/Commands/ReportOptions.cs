namespace Preflight.Cli.Commands;

using Preflight.Cli.Model;
using Preflight.Cli.Parsing;
using Preflight.Cli.Reporting;
using Preflight.Core.History;

/// <summary>
/// Everything <c>report</c> was given.
/// </summary>
/// <remarks>
/// Named required properties rather than a positional record, following
/// <see cref="CommandEnvironment"/>: a future member is then additive instead of
/// breaking every call site. The fourth one is what made
/// that a one-line change.
/// </remarks>
public sealed record ReportOptions
{
    /// <summary>The window, already parsed.</summary>
    public required SinceWindow Since { get; init; }

    /// <summary>Use the ASCII variant of the glyphs.</summary>
    public required bool NoUnicode { get; init; }

    /// <summary>The policy options, for <c>historyPath</c>.</summary>
    public required RunOptions Policy { get; init; }

    /// <summary>
    /// <c>console</c> or <c>json</c>; the parser refuses anything else.
    /// </summary>
    public ReportFormat Format { get; init; } = ReportFormat.Console;
}
