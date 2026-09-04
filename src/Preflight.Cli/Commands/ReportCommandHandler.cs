namespace Preflight.Cli.Commands;

using Preflight.Cli.Model;
using Preflight.Cli.Policy;
using Preflight.Cli.Reporting;
using Preflight.Core.History;

/// <summary>
/// <c>preflight report</c>.
/// </summary>
public static class ReportCommandHandler
{
    public static async Task<int> ExecuteAsync(
        CommandEnvironment environment,
        ReportOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);

        var resolved = await PolicyResolution.ResolveAsync(
            environment.WorkspaceRoot,
            environment.FileSystem,
            environment.Environment,
            [.. environment.Rules.Select(rule => rule.Descriptor)],
            options.Policy,
            environment.Selection,
            cancellationToken,
            environment.ResolvedPackage);

        var settings = HistorySettings.From(resolved.Policy);

        var report = await HistoryReportBuilder.BuildAsync(
            new NdjsonHistoryReader(environment.FileSystem)
                .ReadAsync(HistoryPaths.DirectoryFor(environment.WorkspaceRoot, settings), cancellationToken),
            environment.TimeProvider.GetUtcNow(),
            options.Since.Value,
            cancellationToken);

        Render(environment, options, report);

        // An empty history is a valid answer, not an error. The
        // renderer says so in words; there is no exit code that means "nothing
        // recorded yet" and inventing one would make an ordinary first run look
        // like a failure.
        return ExitCode.Success;
    }

    /// <remarks>
    /// An <c>if</c> on the one value the parser accepts besides the default,
    /// not a three-armed switch. <c>ReportFormat</c> is shared with <c>run</c>
    /// and carries <c>Sarif</c>, which this command's own parser refuses; a
    /// switch would create an arm nothing can reach, and the answer to an
    /// unreachable arm here is not to write it.
    /// </remarks>
    private static void Render(CommandEnvironment environment, ReportOptions options, HistoryReport report)
    {
        if (options.Format == ReportFormat.Json)
        {
            new HistoryReportJsonRenderer(environment.Console.Output).Report(report, options.Since);

            return;
        }

        new HistoryReportRenderer(
                environment.Console,
                GlyphSet.Select(environment.Console, options.NoUnicode))
            .Report(report, options.Since);
    }
}
