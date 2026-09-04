namespace Preflight.Cli.Commands;

using Preflight.Cli.Model;
using Preflight.Core;
using Preflight.Core.History;

/// <summary>
/// Everything <c>measure</c> was given.
/// </summary>
/// <param name="Label">The <c>--label</c> the measurement is filed under.</param>
/// <param name="FileName">The child executable.</param>
/// <param name="Arguments">Its arguments, exactly as typed after the <c>--</c>.</param>
/// <param name="Policy">
/// The policy options, because <c>historyPath</c> and <c>historyMode</c> are
/// policy keys, and this command resolves the same chain a run would.
/// </param>
public sealed record MeasureOptions(
    string Label,
    string FileName,
    IReadOnlyList<string> Arguments,
    RunOptions Policy);
