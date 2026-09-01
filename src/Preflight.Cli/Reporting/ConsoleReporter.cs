namespace Preflight.Cli.Reporting;

using System.Globalization;
using System.Text;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core;

/// <summary>
/// Renders a run for a terminal.
/// </summary>
/// <remarks>
/// <para>
/// The reporter never reorders anything. The order is fixed — level, then rule
/// id, then finding-production order — and the root cause reading before the
/// symptom is what that ordering buys. A formatter that grouped failures first
/// to be "easier to read" would spend it.
/// </para>
/// <para>
/// Every number is formatted with <see cref="CultureInfo.InvariantCulture"/>.
/// The author of this project works on a pt-BR machine, where the default
/// renders <c>0.4s</c> as <c>0,4s</c>, and CI is almost certainly en-US: with
/// the ambient culture, the byte-identical guarantee would hold on each machine
/// separately and fail between them.
/// </para>
/// </remarks>
public sealed class ConsoleReporter
{
    private readonly ConsoleCapabilities _capabilities;
    private readonly GlyphSet _glyphs;

    public ConsoleReporter(ConsoleCapabilities capabilities, GlyphSet glyphs)
    {
        _capabilities = capabilities;
        _glyphs = glyphs;
    }

    /// <summary>
    /// Writes the whole report.
    /// </summary>
    public void Report(
        RunResult result,
        LocalOverlayDecision overlay,
        PipelineSelection selection,
        InstalledPipeline? package = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(overlay);

        var writer = new StringBuilder();

        WriteHeader(writer, result, overlay, selection, package);
        writer.Append('\n');

        foreach (var execution in result.Executions)
        {
            WriteExecution(writer, execution);
        }

        writer.Append('\n');
        WriteSummary(writer, result);

        _capabilities.Output.Write(writer.ToString());
    }

    /// <summary>
    /// Shortens a policy file path to the name the header prints.
    /// </summary>
    /// <remarks>
    /// The header reads <c>policy  base → atlas</c>, not two absolute paths.
    /// The chain carries absolute paths because history has to stay auditable
    /// months later; the header is read at a glance, where a repeated directory
    /// prefix on every element is noise that pushes the part that differs off
    /// the line.
    /// </remarks>
    public static string ShortPolicyName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        return name.StartsWith("preflight.", StringComparison.Ordinal)
            ? name["preflight.".Length..]
            : name;
    }

    private static string DescribeSkip(SkipReason? reason) => reason switch
    {
        SkipReason.DependencyFailed => "(failed, gating)",
        SkipReason.DependencyErrored => "(errored, gating)",
        SkipReason.DependencyDisabled => "(disabled by policy)",
        _ => string.Empty,
    };

    /// <remarks>
    /// The header says so whenever the local overlay is in effect, and calls
    /// the alternative — trusting nobody to forget — the kind of thing that
    /// works until gold week. The two suppressed states are worded apart
    /// because they are different facts: a policy decision that left the file
    /// out, versus there being no file.
    /// </remarks>
    /// <summary>
    /// The pipeline, and where it came from when nobody asked for it.
    /// </summary>
    /// <remarks>
    /// The origin is printed only for <see cref="PipelineSource.Checkout"/>,
    /// and that asymmetry is the point rather than an omission: a run
    /// configured by a file nobody passed must not read the same as one that
    /// was asked for. It is the argument <c>Docs/design.md 6.3</c> makes about
    /// the local overlay, applied to the layer above it. A flag the user typed
    /// needs no explanation, so the bytes of every existing report are
    /// unchanged. See ADR-029.
    /// </remarks>
    private static string DescribePipeline(
        RunResult result, PipelineSelection selection, InstalledPipeline? package)
    {
        var name = (result.Pipeline, selection.Source) switch
        {
            (null, _) => "no pipeline",
            (var pipeline, PipelineSource.Checkout) =>
                $"{pipeline} (from {PolicyResolution.BaseFileName})",
            (var pipeline, _) => pipeline!,
        };

        if (package is null)
        {
            // Byte for byte what this line was before packages existed. A run
            // that never met one has nothing new to say, and every existing
            // golden file depends on it staying silent.
            return name;
        }

        // The version goes beside the name and the reason goes in the
        // parentheses, replacing the selection's own annotation rather than
        // queueing behind it: two bracketed clauses on one line is where a
        // header stops being read.
        return $"{result.Pipeline}@{package.Version} ({DescribeVersionSource(package.Source, selection)})";
    }

    private static string DescribeVersionSource(
        PipelineVersionSource source, PipelineSelection selection) => source switch
        {
            PipelineVersionSource.Pin => "pinned",
            PipelineVersionSource.Requirement => $"from {PipelineRequirement.KeyName}",
            _ => selection.Source is PipelineSource.Checkout
                ? $"from {PolicyResolution.BaseFileName}"
                : "newest installed",
        };

    private static string DescribeOverlay(LocalOverlayDecision overlay) => overlay switch
    {
        { Applied: true } => "local overlay active",
        { Suppressed: LocalOverlaySuppression.CiDetected } =>
            $"local overlay not applied (CI detected: {overlay.CiVariable})",
        { Suppressed: LocalOverlaySuppression.ExplicitlyDisabled } => "local overlay not applied (--no-local)",
        _ => "local overlay not applied",
    };

    private void WriteHeader(
        StringBuilder writer,
        RunResult result,
        LocalOverlayDecision overlay,
        PipelineSelection selection,
        InstalledPipeline? package)
    {
        writer.Append("Preflight ").Append(_glyphs.Separator).Append(' ')
            .Append(StageParser.ToArgument(result.Stage))
            .Append(' ').Append(_glyphs.Separator).Append(' ')
            .Append(DescribePipeline(result, selection, package))
            .Append(' ').Append(_glyphs.Separator).Append(' ')
            .Append(result.Target.Platform)
            .Append('/')
            .Append(result.Target.Configuration)
            .Append('\n');

        var chain = result.PolicyChain.Count == 0
            ? "defaults only"
            : string.Join($" {_glyphs.Arrow} ", result.PolicyChain.Select(ShortPolicyName));

        if (overlay.Applied)
        {
            // The '(!)' is there because an applied
            // local overlay is the one thing in the chain that no reviewer saw.
            chain += $" {_glyphs.Arrow} local (!)";
        }

        writer.Append("policy  ")
            .Append(chain.PadRight(46))
            .Append(DescribeOverlay(overlay));

        // The flags that change what the numbers below mean are stated on the
        // same line as the policy, because both answer "why does this run say
        // what it says". --no-skip can turn a green run red;
        // a reader who cannot see it cannot explain the report.
        if (result.NoSkip)
        {
            writer.Append("   --no-skip in effect");
        }

        if (result.FailOnWarning)
        {
            writer.Append("   --fail-on-warning in effect");
        }

        writer.Append('\n');
    }

    /// <summary>
    /// Wraps <paramref name="text"/> in an ANSI colour, or returns it
    /// untouched.
    /// </summary>
    /// <remarks>
    /// No colour when the output is not an interactive terminal, so a CI log is
    /// not polluted with escape sequences no one will render. The check is on
    /// the injected capability rather than on
    /// <see cref="Console.IsOutputRedirected"/>, which is permanently
    /// <see langword="true"/> under a test host — meaning the coloured branch is
    /// the one no test would reach by accident, and would go unwritten.
    /// </remarks>
    private string Colour(string text, RuleStatus status)
    {
        if (!_capabilities.IsInteractive)
        {
            return text;
        }

        var code = status switch
        {
            RuleStatus.Passed => "32",
            RuleStatus.Warning => "33",
            RuleStatus.Failed => "31",
            RuleStatus.Errored => "35",
            _ => "90",
        };

        return $"\u001b[{code}m{text}\u001b[0m";
    }

    private void WriteExecution(StringBuilder writer, RuleExecution execution)
    {
        var glyph = _glyphs.For(execution.Status).PadRight(_glyphs.Width);
        var trailing = execution.Status == RuleStatus.Skipped
            ? "skipped"
            : DurationFormat.Seconds(execution.Duration);

        writer.Append("  ")
            .Append(Colour(glyph, execution.Status))
            .Append("  ")
            .Append(execution.RuleId.Value.PadRight(48))
            .Append(trailing.PadLeft(8));

        // This is the condition on which the whole cache is
        // acceptable: a result that did not come from this run says so. Without
        // it the report claims a check ran when it did not, which is the one
        // thing an accelerated tool must never do.
        if (execution.FromCache)
        {
            writer.Append("  (cached)");
        }

        writer.Append('\n');

        foreach (var finding in execution.Findings)
        {
            WriteFinding(writer, finding);
        }

        WriteSkipAttribution(writer, execution);

        if (execution.ErrorDetail is { } detail)
        {
            writer.Append("     ").Append(detail).Append('\n');
        }
    }

    /// <remarks>
    /// Every member below <c>Message</c> is independently optional, so a rule
    /// may legally produce a finding with nothing but a message — which is what
    /// a rule written in a hurry produces. Each line is emitted only when it
    /// has something to say; a row of empty labels would be a template
    /// pretending to be evidence.
    /// </remarks>
    private static void WriteFinding(StringBuilder writer, Finding finding)
    {
        writer.Append("     ").Append(finding.Message).Append('\n');

        // 'at' is deliberately not in the same column as the three below it.
        // Expected, actual and fix are aligned with each other because a
        // reader compares them; the location is a different kind of fact and
        // sits on its own indent, exactly as the documented example draws it.
        if (finding.Location is { } location)
        {
            writer.Append("       at  ").Append(Describe(location)).Append('\n');
        }

        AppendLabelled(writer, "expected", finding.Expected);
        AppendLabelled(writer, "actual", finding.Actual);

        // 'fix' rather than 'remediation': the shorter word, and
        // it says a rule that fails without saying how to fix it delivers half
        // the work.
        AppendLabelled(writer, "fix", finding.Remediation);
    }

    private static void AppendLabelled(StringBuilder writer, string label, string? value)
    {
        if (value is not null)
        {
            writer.Append("       ").Append(label.PadRight(10)).Append(value).Append('\n');
        }
    }

    private static string Describe(FindingLocation location) => location switch
    {
        { Line: { } line, Column: { } column } =>
            $"{location.RelativePath}:{line.ToString(CultureInfo.InvariantCulture)}:{column.ToString(CultureInfo.InvariantCulture)}",
        { Line: { } line } => $"{location.RelativePath}:{line.ToString(CultureInfo.InvariantCulture)}",
        _ => location.RelativePath,
    };

    /// <remarks>
    /// Every cause is printed, in the order the engine gave them — by
    /// topological level, so the most likely root comes first; printing only
    /// the first element would throw the ordering away, and re-sorting it
    /// alphabetically to look tidy would undo the ADR entirely.
    /// </remarks>
    private static void WriteSkipAttribution(StringBuilder writer, RuleExecution execution)
    {
        if (execution.SkippedBecauseOf.Count == 0)
        {
            return;
        }

        var causes = string.Join(", ", execution.SkippedBecauseOf.Select(id => id.Value));

        writer.Append("     blocked by  ")
            .Append(causes)
            .Append("   ")
            .Append(DescribeSkip(execution.SkipReason))
            .Append('\n');
    }

    private void WriteSummary(StringBuilder writer, RunResult result)
    {
        var separator = _glyphs.Separator;

        var counts = result.Executions
            .GroupBy(execution => execution.Status)
            .ToDictionary(group => group.Key, group => group.Count());

        var parts = new List<string>();

        AppendCount(parts, counts, RuleStatus.Errored, "errored");
        AppendCount(parts, counts, RuleStatus.Failed, "failed");
        AppendCount(parts, counts, RuleStatus.Skipped, "skipped");
        AppendCount(parts, counts, RuleStatus.Warning, "warning");
        AppendCount(parts, counts, RuleStatus.NotApplicable, "n/a");
        AppendCount(parts, counts, RuleStatus.Passed, "passed");

        writer.Append("  ").Append(Describe(result.Verdict));

        // A run that executed nothing must not read as an ordinary
        // success. The line has to be greppable, because that is how someone
        // discovers an overlay that disabled everything.
        writer.Append(parts.Count == 0
            ? $" {separator} 0 rules executed (all selected rules disabled by policy)"
            : $" {separator} " + string.Join(", ", parts));

        writer.Append(" in ").Append(DurationFormat.Seconds(result.Duration)).Append('\n');
    }

    private static void AppendCount(
        List<string> parts,
        Dictionary<RuleStatus, int> counts,
        RuleStatus status,
        string label)
    {
        if (counts.TryGetValue(status, out var count))
        {
            parts.Add($"{count.ToString(CultureInfo.InvariantCulture)} {label}");
        }
    }

    private static string Describe(RunVerdict verdict) => verdict switch
    {
        RunVerdict.Passed => "Passed",
        RunVerdict.PassedWithWarnings => "Passed with warnings",
        RunVerdict.Blocked => "Blocked",
        RunVerdict.Errored => "Errored",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Unmapped run verdict."),
    };
}
