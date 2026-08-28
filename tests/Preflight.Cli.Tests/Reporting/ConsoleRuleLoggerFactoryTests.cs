namespace Preflight.Cli.Tests.Reporting;

using Preflight.Abstractions;
using Preflight.Cli.Reporting;

/// <summary>
/// Fixes the one log sink that ships.
/// </summary>
/// <remarks>
/// None of the six built-in rules logs anything, so nothing else in the suite
/// exercises this. A factory that dropped every line, or put one rule's id on
/// another rule's message, would leave the whole project green and be
/// discovered by whoever first wrote a rule that logs.
/// </remarks>
public sealed class ConsoleRuleLoggerFactoryTests
{
    private static readonly RuleId Large = new("core.presubmit.large-file");
    private static readonly RuleId Toolchain = new("core.workspace.toolchain");

    [Fact]
    public void Info_WritesTheLevelTheIdAndTheMessage()
    {
        var sink = new StringWriter();

        new ConsoleRuleLoggerFactory(sink).ForRule(Large).Info("examined 4 files");

        var line = sink.ToString();

        line.ShouldContain("info");
        line.ShouldContain("core.presubmit.large-file");
        line.ShouldContain("examined 4 files");
    }

    [Fact]
    public void Warn_WritesItsOwnLevel()
    {
        var sink = new StringWriter();

        new ConsoleRuleLoggerFactory(sink).ForRule(Large).Warn("slow");

        sink.ToString().ShouldContain("warn");
    }

    /// <remarks>
    /// Off by default. A rule's debug output is for whoever is writing the
    /// rule, and the console report spends the console budget on the report.
    /// </remarks>
    [Fact]
    public void Debug_IsSilentUnlessVerbose()
    {
        var quiet = new StringWriter();
        var verbose = new StringWriter();

        new ConsoleRuleLoggerFactory(quiet).ForRule(Large).Debug("internals");
        new ConsoleRuleLoggerFactory(verbose, verbose: true).ForRule(Large).Debug("internals");

        quiet.ToString().ShouldBeEmpty();
        verbose.ToString().ShouldContain("internals");
    }

    /// <summary>
    /// Each logger carries its own rule's id and no other.
    /// </summary>
    /// <remarks>
    /// Scoped at construction rather than by the caller passing an id, because
    /// a logger that trusted the caller would put one rule's id on another
    /// rule's line the first time somebody copied a call.
    /// </remarks>
    [Fact]
    public void ForRule_ScopesEachLoggerToItsOwnId()
    {
        var sink = new StringWriter();
        var factory = new ConsoleRuleLoggerFactory(sink);

        factory.ForRule(Large).Info("first");
        factory.ForRule(Toolchain).Info("second");

        var lines = sink.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].ShouldContain("core.presubmit.large-file");
        lines[0].ShouldNotContain("core.workspace.toolchain");
        lines[1].ShouldContain("core.workspace.toolchain");
    }

    /// <summary>
    /// Concurrent writes never interleave mid-line.
    /// </summary>
    /// <remarks>
    /// The context services requires serialised writes and the concurrency
    /// contract runs rules at the same level concurrently. Without the lock two
    /// rules produce a line that describes neither — and the failure is
    /// intermittent, which is the kind that gets dismissed rather than fixed.
    /// </remarks>
    [Fact]
    public void Write_FromManyThreads_ProducesWholeLines()
    {
        var sink = new StringWriter();
        var factory = new ConsoleRuleLoggerFactory(sink);

        Parallel.For(0, 200, index =>
            factory.ForRule(index % 2 == 0 ? Large : Toolchain).Info($"message {index}"));

        var lines = sink.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Length.ShouldBe(200);
        lines.ShouldAllBe(line => line.Contains("info", StringComparison.Ordinal));
        lines.ShouldAllBe(line => line.Contains("message", StringComparison.Ordinal));
    }
}
