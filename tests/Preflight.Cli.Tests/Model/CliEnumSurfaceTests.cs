namespace Preflight.Cli.Tests.Model;

using Preflight.Cli.Model;

/// <summary>
/// Pins the exact member set of the CLI's own closed enums.
/// </summary>
/// <remarks>
/// The mirror of <c>EnumSurfaceTests</c> in <c>Core.Tests</c>, for the two enums
/// the command surface fixes rather than the rule contract. These are not part of the plugin
/// surface the plugin version contract versions, but they are the vocabulary the parser, the
/// handlers and every reporter share — and a value removed or renamed should be
/// a failing test rather than silence.
/// </remarks>
public sealed class CliEnumSurfaceTests
{
    [Fact]
    public void ReportFormat_DefinesExactlyConsoleJsonAndSarif() =>
        Enum.GetNames<ReportFormat>().ShouldBe(["Console", "Json", "Sarif"], ignoreOrder: true);

    [Fact]
    public void GraphFormat_DefinesExactlyTextAndDot() =>
        Enum.GetNames<GraphFormat>().ShouldBe(["Text", "Dot"], ignoreOrder: true);
}
