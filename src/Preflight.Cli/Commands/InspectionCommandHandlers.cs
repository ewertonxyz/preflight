namespace Preflight.Cli.Commands;

using System.Globalization;
using System.Text;
using Preflight.Abstractions.Rules;
using Preflight.Cli.Model;
using Preflight.Cli.Parsing;
using Preflight.Cli.Policy;
using Preflight.Cli.Reporting;
using Preflight.Core;
using Preflight.Core.Execution;
using Preflight.Core.Graph;
using Preflight.Core.Policy;

/// <summary>
/// <c>preflight rules</c>, <c>graph</c> and <c>explain</c>.
/// </summary>
/// <remarks>
/// The three commands that answer questions rather than validate anything. They
/// share a shape — resolve the policy, build the graph, print — and they share
/// the same exit codes: 0 or 2, never 1, because none of them can reject code.
/// </remarks>
public static class InspectionCommandHandlers
{
    /// <summary>
    /// Lists the discovered rules with the policy actually in force.
    /// </summary>
    /// <remarks>
    /// A disabled rule appears, marked. Dropping it would answer the question
    /// "which rules exist" with "which rules will run", and the gap between
    /// those two is exactly what someone runs this command to see.
    /// </remarks>
    public static async Task<int> RulesAsync(
        CommandEnvironment environment,
        RunOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var descriptors = Descriptors(environment);
        var resolved = await Resolve(environment, descriptors, options, cancellationToken);
        var graph = RuleGraph.Build(descriptors);
        var writer = new StringBuilder();

        foreach (var (level, index) in graph.Levels.Select((level, index) => (level, index)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var id in level)
            {
                var descriptor = descriptors.Single(candidate => candidate.Id == id);
                var enabled = resolved.Policy.RuleValue<bool>(id, "enabled").Value;

                writer.Append(CultureInfo.InvariantCulture, $"  {(enabled ? " " : "-")} {id.Value,-38}")
                    .Append(CultureInfo.InvariantCulture, $" {StageParser.ToArgument(descriptor.Stage),-16}")
                    .Append(CultureInfo.InvariantCulture, $" level {index}")
                    .Append(enabled ? string.Empty : "   disabled by policy")
                    .Append('\n');
            }
        }

        environment.Console.Output.Write(writer.ToString());

        return ExitCode.Success;
    }

    /// <summary>
    /// Prints the dependency graph.
    /// </summary>
    /// <remarks>
    /// By topological level, ordinal within a level. The point of this command
    /// is being diffable, so the order is the output rather than a detail of
    /// it, and both formats inherit it whole.
    /// </remarks>
    public static int Graph(
        CommandEnvironment environment,
        GraphFormat format,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var descriptors = Descriptors(environment);
        var graph = RuleGraph.Build(descriptors);

        if (format == GraphFormat.Dot)
        {
            new DotGraphRenderer(environment.Console.Output).Render(graph, descriptors);

            return ExitCode.Success;
        }

        WriteGraphAsText(environment, descriptors, graph, cancellationToken);

        return ExitCode.Success;
    }

    /// <remarks>
    /// The body this method had before <c>--format</c> existed, moved without a
    /// byte changing. <c>text</c> is the default, and a reader diffing today's
    /// output against yesterday's has to see nothing at all.
    /// </remarks>
    private static void WriteGraphAsText(
        CommandEnvironment environment,
        IReadOnlyList<RuleDescriptor> descriptors,
        RuleGraph graph,
        CancellationToken cancellationToken)
    {
        var writer = new StringBuilder();

        foreach (var (level, index) in graph.Levels.Select((level, index) => (level, index)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            writer.Append(CultureInfo.InvariantCulture, $"level {index}\n");

            foreach (var id in level)
            {
                var dependsOn = descriptors.Single(candidate => candidate.Id == id).DependsOn;

                writer.Append(CultureInfo.InvariantCulture, $"  {id.Value}")
                    .Append(dependsOn.Count == 0
                        ? string.Empty
                        : "  <- " + string.Join(", ", dependsOn.Select(dependency => dependency.Value)))
                    .Append('\n');
            }
        }

        environment.Console.Output.Write(writer.ToString());
    }

    /// <summary>
    /// Explains how one rule's effective policy was resolved.
    /// </summary>
    /// <remarks>
    /// The command policy precedence promises: in a chain of four overlays,
    /// "why is this limit 4096?" has to have an answer in one command rather
    /// than in an archaeology of JSON files.
    /// </remarks>
    public static async Task<int> ExplainAsync(
        CommandEnvironment environment,
        RunOptions options,
        string? ruleIdText,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var descriptors = Descriptors(environment);
        var ruleId = ResolveRuleId(ruleIdText, descriptors);
        var descriptor = descriptors.Single(candidate => candidate.Id == ruleId);
        var resolved = await Resolve(environment, descriptors, options, cancellationToken);
        var graph = RuleGraph.Build(descriptors);

        var writer = new StringBuilder();

        writer.Append(CultureInfo.InvariantCulture, $"{ruleId.Value} — {descriptor.DisplayName}\n")
            .Append(CultureInfo.InvariantCulture, $"  stage        {StageParser.ToArgument(descriptor.Stage)}\n")
            .Append(CultureInfo.InvariantCulture, $"  depends on   {Join(graph.TransitiveDependenciesOf(ruleId))}\n")
            .Append(CultureInfo.InvariantCulture, $"  dependents   {Join(graph.TransitiveDependentsOf(ruleId))}\n");

        if (descriptor.Documentation is { } documentation)
        {
            writer.Append(CultureInfo.InvariantCulture, $"  docs         {documentation}\n");
        }

        writer.Append("\nEffective policy\n")
            .Append(CultureInfo.InvariantCulture, $"  {"key",-20} {"value",-11} origin\n");

        foreach (var entry in resolved.Policy.RuleEntries(ruleId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            writer.Append(CultureInfo.InvariantCulture, $"  {entry.Key,-20} {Render(entry.Value.Value),-11} ")
                .Append(DescribeOrigin(entry.Value.Origin))
                .Append('\n');

            // One overrides line, naming the layer this
            // value replaced and what it said. The history holds every layer;
            // showing all of them would turn a four-overlay chain into a wall
            // and bury the one comparison the reader came for.
            if (entry.Value.History.Count > 0)
            {
                var previous = entry.Value.History[^1];

                writer.Append(CultureInfo.InvariantCulture, $"  {string.Empty,-32} ")
                    .Append(CultureInfo.InvariantCulture, $"overrides {DescribeOrigin(previous.Origin)} ({Render(previous.Value)})")
                    .Append('\n');
            }
        }

        writer.Append('\n')
            .Append(CultureInfo.InvariantCulture, $"Policy chain         {Chain(resolved)}\n")
            .Append(CultureInfo.InvariantCulture, $"Local overlay        {Overlay(resolved.Overlay)}\n");

        environment.Console.Output.Write(writer.ToString());

        return ExitCode.Success;
    }

    private static IReadOnlyList<RuleDescriptor> Descriptors(CommandEnvironment environment) =>
        [.. environment.Rules.Select(rule => rule.Descriptor)];

    private static Task<ResolvedPolicy> Resolve(
        CommandEnvironment environment,
        IReadOnlyList<RuleDescriptor> descriptors,
        RunOptions options,
        CancellationToken cancellationToken) =>
        PolicyResolution.ResolveAsync(
            environment.WorkspaceRoot,
            environment.FileSystem,
            environment.Environment,
            descriptors,
            options,
            environment.Selection,
            cancellationToken,
            environment.ResolvedPackage);

    /// <remarks>
    /// The id is validated before it is constructed, because
    /// <see cref="RuleId"/> throws on a malformed one — and an uncaught
    /// <see cref="ArgumentException"/> leaves the process at exit 3 with a stack
    /// trace, claiming an internal error for a typo the user can fix.
    /// </remarks>
    private static RuleId ResolveRuleId(string? text, IReadOnlyList<RuleDescriptor> descriptors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new RuleDiscoveryException("explain needs a rule id. Try 'preflight rules' to see them.");
        }

        RuleId ruleId;

        try
        {
            ruleId = new RuleId(text);
        }
        catch (ArgumentException exception)
        {
            throw new RuleDiscoveryException(exception.Message);
        }

        if (descriptors.Any(descriptor => descriptor.Id == ruleId))
        {
            return ruleId;
        }

        var suggestions = SuggestionFinder.FindClosest(text, descriptors.Select(d => d.Id.Value));

        throw new RuleDiscoveryException(
            suggestions.Count == 0
                ? $"No rule with id '{text}' was discovered."
                : $"No rule with id '{text}' was discovered. Did you mean {string.Join(" or ", suggestions.Select(id => $"'{id}'"))}?");
    }

    private static string Join(IReadOnlyList<RuleId> ids) =>
        ids.Count == 0 ? "—" : string.Join(", ", ids.Select(id => id.Value));

    private static string Chain(ResolvedPolicy resolved) =>
        resolved.Chain.Count == 0
            ? "defaults only"
            : string.Join(" → ", resolved.Chain.Select(ConsoleReporter.ShortPolicyName));

    private static string Overlay(LocalOverlayDecision overlay) => overlay switch
    {
        { Applied: true } => "applied",
        { Suppressed: LocalOverlaySuppression.CiDetected } => $"not applied (CI detected: {overlay.CiVariable})",
        { Suppressed: LocalOverlaySuppression.ExplicitlyDisabled } => "not applied (--no-local)",
        _ => "not applied (no local file)",
    };

    /// <remarks>
    /// Invariant, and lower-cased booleans. The value shown has to be the value
    /// somebody would type back into the JSON, and <c>True</c> is not valid
    /// JSON — a report that prints one teaches a wrong edit.
    /// </remarks>
    private static string Render(object? value) => value switch
    {
        null => "null",
        bool boolean => boolean ? "true" : "false",
        string text => text,
        IEnumerable<string> items => "[" + string.Join(", ", items) + "]",

        // A cast, not a conversion with a fallback. The arms above take null,
        // strings and lists, so what reaches here is a bool or a long — both
        // IFormattable, both returning a non-null string. A null-coalescing
        // fallback beside it would be a branch no value can take, and a cast
        // that one day fails says so loudly instead of printing an empty cell.
        _ => ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),
    };

    /// <remarks>
    /// <see cref="PolicyOrigin.FromRootKey"/> is recursive, and the report
    /// prints it as the source plus the root key that cascaded into this rule:
    /// <c>preflight.base.json:5   (root defaultTimeoutSeconds)</c>. Flattening
    /// it to just the file would drop the half that explains why a rule has a
    /// value nobody wrote for it.
    /// </remarks>
    /// <summary>
    /// Renders where one effective value came from.
    /// </summary>
    /// <remarks>
    /// Public because <c>InspectionCommandHandlersTests</c> enumerates
    /// <see cref="PolicyOrigin"/> by reflection and asserts that every variant
    /// renders distinctly — see the note on the discard below. What a test
    /// exercises is public in this project; there is no
    /// <c>InternalsVisibleTo</c> anywhere in it, because a contract reached
    /// through one is a contract that reads as private and behaves as public.
    /// </remarks>
    public static string DescribeOrigin(PolicyOrigin origin) => origin switch
    {
        PolicyOrigin.FromFile file =>
            $"{ConsoleReporter.ShortPolicyName(file.FilePath)}.json:{file.Line.ToString(CultureInfo.InvariantCulture)}",
        PolicyOrigin.FromCommandLine => "command line",
        PolicyOrigin.FromRootKey rootKey => $"{DescribeOrigin(rootKey.Source)}   (root {rootKey.RootKey})",
        PolicyOrigin.FromTarget target => $"{DescribeOrigin(target.Source)}   (target {target.TargetKey})",

        // Qualifies the file rather than annotating after it, because the path
        // it wraps does not exist in the checkout: a reader who sees
        // "acme.json:8" goes looking in the workspace and finds nothing. The
        // package and version go in front, where the reader meets them before
        // the file name.
        PolicyOrigin.FromPackage package =>
            $"{package.Pipeline}@{package.Version}/{DescribeOrigin(package.Source)}",
        PolicyOrigin.DescriptorDefault => "RuleDescriptor default",

        // EngineDefault is the discard rather than a named arm, because
        // PolicyOrigin is a closed hierarchy and a switch expression still
        // demands a discard it cannot prove unreachable. Naming the last one
        // and adding a throw beside it puts a permanent hole in the branch
        // count; letting it be the discard does not.
        //
        // What that costs is a variant added later rendering as an engine
        // default, silently and with no test failing — so the guard lives in
        // InspectionCommandHandlersTests, which walks the hierarchy by
        // reflection. The switch stays honest about coverage; the test stays
        // honest about the hierarchy.
        _ => "engine default",
    };
}
