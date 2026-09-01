namespace Preflight.Cli;

using System.CommandLine;
using System.CommandLine.Parsing;
using Preflight.Abstractions.Model;
using Preflight.Cli.Commands;
using Preflight.Cli.Model;
using Preflight.Cli.Parsing;
using Preflight.Cli.Pipelines;
using Preflight.Cli.Policy;
using Preflight.Core;
using Preflight.Core.Policy;

/// <summary>
/// Turns one parsed command line into a command that has run.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="PreflightCommandLine"/>, which declares what the
/// tool accepts. The two change for different reasons — a new flag edits the
/// surface, a new handler edits this — and holding both in one type meant a
/// file of eleven hundred lines where a reader looking for the meaning of
/// <c>--since</c> had to scan past every command that exists.
/// </para>
/// <para>
/// Everything here reads a <c>ParseResult</c>, and reading one is narrower than
/// it looks: <c>GetValue</c> throws for a name no symbol declares, so every
/// option that only some commands carry is asked for through
/// <see cref="Declares"/> first. That is why the command name is resolved
/// before any option is read.
/// </para>
/// </remarks>
public static class CommandDispatcher
{
    /// <summary>
    /// Runs the parsed command against <paramref name="environment"/>.
    /// </summary>
    public static int Run(ParseResult parse, CommandEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(environment);

        var name = parse.CommandResult.Command.Name;

        // The name is resolved before any option is read, and reversing that
        // order breaks the refusal: ParseResult.GetValue throws for a name no
        // symbol declares, so reading run's options while dispatching an unknown
        // command replaces this method's own error with the library's.

        // Before the command runs, and before any policy is resolved, so that
        // a policy naming a plugin's rule can be validated against it. The
        // load contexts live exactly as
        // long as the command that executes their rules.
        using var loader = environment.AssemblyLoader();

        // Before composition, because the package contributes rules, and before
        // policy resolution, because it contributes the policy. One answer,
        // handed to both — including to graph, which resolves no policy and
        // still has to draw every rule the run would execute.
        //
        // The pipeline subcommands are excluded: install writes the very thing
        // this would read, and declare exists precisely because the checkout
        // does not yet say which pipeline it is.
        var managesPackages = ManagesPackages(name);

        // Read once here and carried, because the selection below and the
        // package resolution after it both come out of this one file.
        var checkout = managesPackages
            ? CheckoutDocument.Absent
            : CheckoutDocument.Read(environment.WorkspaceRoot, environment.FileSystem);

        environment = environment with
        {
            Checkout = checkout,
            Selection = managesPackages
                ? PipelineSelection.None
                : PipelineSelector.Select(
                    environment.WorkspaceRoot,
                    environment.FileSystem,
                    PipelineOf(parse),
                    checkout,
                    CancellationToken.None),
        };

        environment = environment with
        {
            ResolvedPackage = managesPackages
                ? null
                : PackageResolution.For(environment, CancellationToken.None),
        };

        environment = environment with
        {
            Rules = PluginLoading.Compose(environment, loader, RulesPathsOf(parse)),
        };

        return name switch
        {
            "run" => Sync(RunCommandHandler.ExecuteAsync(
                environment, RunOptionsFrom(parse), CancellationToken.None)),
            "rules" => Sync(InspectionCommandHandlers.RulesAsync(
                environment, PolicyOptionsFrom(parse), CancellationToken.None)),

            // The one handler that is already synchronous: it reads a graph
            // this process has in memory and writes it out.
            "graph" => InspectionCommandHandlers.Graph(
                environment, GraphFormatFrom(parse), CancellationToken.None),

            // The subcommand's own name, as 'clear' is for cache.
            "workspace" => Sync(CreateCommandHandler.WorkspaceAsync(
                environment, CancellationToken.None)),
            "rule" => Sync(CreateCommandHandler.RuleAsync(
                environment, parse.GetValue<string>("id")!, CancellationToken.None)),
            "policy" => Sync(CreateCommandHandler.PolicyAsync(
                environment, parse.GetValue<string>("name")!, CancellationToken.None)),

            // The six pipeline subcommands, by their own names, as 'workspace'
            // and 'clear' already are. 'pipeline' alone is a parse error before
            // it reaches here.
            "declare" => Sync(PipelineCommandHandler.DeclareAsync(
                environment, parse.GetValue<string?>("name"), CancellationToken.None)),
            "use" => Sync(PipelineCommandHandler.UseAsync(
                environment, parse.GetValue<string?>("selector"), CancellationToken.None)),
            "list" => Sync(PipelineCommandHandler.ListAsync(
                environment, CancellationToken.None)),
            "pack" => Sync(PipelinePackager.PackAsync(
                environment,
                parse.GetValue<string>("source")!,
                parse.GetValue<string>("--output")!,
                CancellationToken.None)),
            "validate" => Sync(PipelineValidator.ValidateAsync(
                environment, parse.GetValue<string>("source")!, CancellationToken.None)),
            "install" => Sync(PipelineInstaller.InstallAsync(
                environment,
                parse.GetValue<string>("package")!,
                parse.GetValue<int?>("--keep"),
                parse.GetValue<bool>("--no-gc"),
                CancellationToken.None)),
            "measure" => Sync(MeasureCommandHandler.ExecuteAsync(
                environment, MeasureOptionsFrom(parse), CancellationToken.None)),
            "report" => Sync(ReportCommandHandler.ExecuteAsync(
                environment, ReportOptionsFrom(parse), CancellationToken.None)),

            // The subcommand's own name, because that is what the parser
            // resolves to. 'cache' alone is a parse error before it reaches
            // here, so 'clear' is unambiguous.
            "clear" => Sync(CacheCommandHandler.ClearAsync(
                environment, PolicyOptionsFrom(parse), CancellationToken.None)),
            "explain" => Sync(InspectionCommandHandlers.ExplainAsync(
                environment,
                PolicyOptionsFrom(parse),
                parse.GetValue<string?>("<rule-id>"),
                CancellationToken.None)),
            _ => NotACommand(name),
        };
    }

    /// <summary>
    /// Runs an asynchronous handler on this thread and returns its exit code.
    /// </summary>
    /// <remarks>
    /// Synchronous on purpose, and named so that the switch above says what it
    /// dispatches rather than how it waits. <c>Main</c> returns an int and the
    /// whole tool is one run; an async entry point would add a state machine to
    /// every command in exchange for nothing, because no other thread is
    /// waiting on this one.
    /// </remarks>
    private static int Sync(Task<int> handler) => handler.GetAwaiter().GetResult();

    /// <summary>
    /// Whether this command manages packages rather than consuming one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked of the set the command surface built, not of a list written out
    /// again here. Every subcommand of <c>pipeline</c> manages packages, so a
    /// seventh one added later is covered without this method being touched;
    /// the literal list this replaced named the six that existed, and a
    /// seventh would have resolved a package in silence.
    /// </para>
    /// <para>
    /// Resolving a package before <c>install</c> writes it would refuse the
    /// very command that fixes the refusal, and <c>declare</c> exists because
    /// the checkout does not yet say which pipeline it is. <c>pack</c> and
    /// <c>validate</c> work on a source tree named as an argument, so the
    /// package a surrounding checkout happens to resolve to is not theirs and
    /// refusing on it would make an author's tooling depend on which directory
    /// they ran it from.
    /// </para>
    /// </remarks>
    private static bool ManagesPackages(string command) =>
        PreflightCommandLine.PackageManagingCommands.Contains(command);

    /// <remarks>
    /// Only the commands that declare the option carry one. Reading it off a
    /// command that does not — <c>graph</c>, or a pipeline subcommand — throws
    /// inside the parser rather than returning null.
    /// </remarks>
    private static string? PipelineOf(ParseResult parse) =>
        Declares(parse, CommandLineNames.PipelineOption) ? PipelineFrom(parse) : null;

    /// <summary>
    /// Every <c>--rules-path</c> the command was given.
    /// </summary>
    /// <remarks>
    /// Guarded rather than read directly, and for the same reason the command
    /// name is resolved before any option: <see cref="Run"/> is public and
    /// takes any parse result, and <c>ParseResult.GetValue</c> throws for a
    /// name no symbol declares. Without the guard, a parse result this file
    /// does not recognise would fail here with the library's exception instead
    /// of reaching the arm written to refuse it — which would make that arm
    /// unreachable and its message a fiction.
    ///
    /// Every command the root builds declares the option, so the empty result
    /// is only ever produced for a command that is about to be refused.
    /// </remarks>
    private static string[] RulesPathsOf(ParseResult parse) =>
        Declares(parse, CommandLineNames.RulesPathOption) ? MultiValued(parse, CommandLineNames.RulesPathOption) : [];

    /// <summary>
    /// Whether the command being dispatched declares <paramref name="option"/>.
    /// </summary>
    /// <remarks>
    /// Asked before every read of an option that only some commands carry.
    /// <c>ParseResult.GetValue</c> throws for a name no symbol declares, so
    /// reading <c>--pipeline</c> off <c>graph</c> raises out of the parser
    /// instead of returning null, and the refusal this file wrote is replaced
    /// by the library's exception.
    /// </remarks>
    private static bool Declares(ParseResult parse, string option) =>
        parse.CommandResult.Command.Options.Any(
            declared => string.Equals(declared.Name, option, StringComparison.Ordinal));

    /// <remarks>
    /// Unreachable, and excluded rather than covered: the parser rejects an
    /// unknown command before dispatch, so nothing reaches this method that is
    /// not one of the names the switch above already answers. It stays as a
    /// throw rather than a silent zero, because a command added to the surface
    /// and forgotten in that switch would otherwise report success having done
    /// nothing.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static int NotACommand(string name) =>
        throw new RuleDiscoveryException($"'{name}' is not a command.");

    /// <summary>
    /// Everything <c>run</c> was given.
    /// </summary>
    /// <remarks>
    /// <c>--stage</c> is required here, so the fallback below is unreachable
    /// through the parser and exists because <see cref="StageParser.Parse"/>
    /// returns a nullable rather than throwing. <c>--platform</c> and
    /// <c>--configuration</c> do have defaults: a workspace-stage run has no
    /// target to speak of, and refusing one would make the flags mandatory for
    /// two stages that never read them.
    /// </remarks>
    private static RunOptions RunOptionsFrom(ParseResult parse) => new()
    {
        Stage = RequiredStage(parse.GetValue<string?>("--stage")),
        Target = StatedTargetFrom(parse),
        Pipeline = PipelineFrom(parse),
        ChangedFrom = parse.GetValue<string?>("--changed-from"),
        Format = ReportFormatFrom(parse),
        NoSkip = parse.GetValue<bool>("--no-skip"),
        FailOnWarning = parse.GetValue<bool>("--fail-on-warning"),
        NoUnicode = parse.GetValue<bool>("--no-unicode"),
        NoCache = parse.GetValue<bool>("--no-cache"),
        NoLocal = parse.GetValue<bool>("--no-local"),
        AllowLocal = parse.GetValue<bool>("--allow-local"),
        SetOverrides = MultiValued(parse, "--set"),
    };

    /// <summary>
    /// A multi-valued symbol's tokens, never null.
    /// </summary>
    /// <remarks>
    /// The null branch exists only because <c>ParseResult.GetValue</c> is
    /// annotated as possibly returning one. A multi-valued symbol that was
    /// never given parses to an empty array in every version this has been run
    /// against, so the branch is unreachable in practice while remaining
    /// required by the signature — the same shape as <c>PolicyLoader</c>'s
    /// handling of <c>JsonException.LineNumber</c>, and excluded for the same
    /// reason: a fabricated test would assert something about the library
    /// rather than about this code.
    ///
    /// One helper for both <c>--set</c> and <c>measure</c>'s command tokens. A
    /// second copy of this would be a second exclusion, and an exclusion per
    /// call site is how a project stops noticing them.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static string[] MultiValued(ParseResult parse, string name) =>
        parse.GetValue<string[]?>(name) ?? [];

    /// <summary>
    /// Everything <c>measure</c> was given.
    /// </summary>
    /// <remarks>
    /// Both null-forgiving operators are guaranteed by the command's validator,
    /// which has already turned a missing <c>--label</c> and a missing command
    /// into parse errors — and therefore into exit 2, before this method exists
    /// to be called. A defensive branch here would be one no input can reach.
    /// </remarks>
    private static MeasureOptions MeasureOptionsFrom(ParseResult parse)
    {
        var command = MultiValued(parse, CommandLineNames.MeasureCommandArgument);

        return new MeasureOptions(
            parse.GetValue<string?>("--label")!,
            command[0],
            [.. command.Skip(1)],
            PolicyOptionsFrom(parse));
    }

    /// <summary>
    /// Everything <c>report</c> was given.
    /// </summary>
    /// <remarks>
    /// The window is parsed twice: once by the validator, to refuse a malformed
    /// one with exit 2, and once here. Threading the first result through would
    /// mean the parser held state between the two phases, and the whole
    /// arrangement is that a refusal is a parse error like any other.
    /// </remarks>
    private static ReportOptions ReportOptionsFrom(ParseResult parse) => new()
    {
        Since = SinceDuration.Parse(parse.GetValue<string?>("--since"))!,
        NoUnicode = parse.GetValue<bool>("--no-unicode"),
        Policy = PolicyOptionsFrom(parse),
        Format = ReportFormatFrom(parse),
    };

    /// <summary>
    /// The report format the command was given, or the default.
    /// </summary>
    /// <remarks>
    /// The comparison is ordinal and the parser has already refused every token
    /// that is not one of the accepted values, so the last arm is reached only
    /// by the absent option. That is what makes the default a default rather
    /// than a silent fallback for a misspelling — which is exactly what this
    /// method used to be.
    /// </remarks>
    private static ReportFormat ReportFormatFrom(ParseResult parse) =>
        parse.GetValue<string?>(CommandLineNames.FormatOption) switch
        {
            "json" => ReportFormat.Json,
            "sarif" => ReportFormat.Sarif,
            _ => ReportFormat.Console,
        };

    /// <summary>
    /// The graph format the command was given, or the default.
    /// </summary>
    /// <remarks>
    /// <c>text</c> is the default and its output is byte-identical to what the
    /// command printed before the option existed.
    /// </remarks>
    private static GraphFormat GraphFormatFrom(ParseResult parse) =>
        parse.GetValue<string?>(CommandLineNames.FormatOption) switch
        {
            "dot" => GraphFormat.Dot,
            _ => GraphFormat.Text,
        };

    /// <remarks>
    /// The fallback is unreachable and excluded rather than covered:
    /// <c>--stage</c> is required and constrained to three values, so the
    /// parser has already refused anything <see cref="StageParser.Parse"/>
    /// would return null for. It throws rather than defaulting, because a
    /// default here would silently run a stage nobody asked for — the one thing
    /// this file spends option's ergonomics to prevent.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static ValidationStage RequiredStage(string? value) =>
        StageParser.Parse(value)
            ?? throw new RuleDiscoveryException($"'{value}' is not a stage.");

    /// <summary>
    /// The subset an inspection command was given.
    /// </summary>
    /// <remarks>
    /// Only the four options <c>WithPolicyOptions</c> declares are read,
    /// because only those exist on these commands and asking for a name no
    /// symbol declares throws. The stage and the target are filled with values
    /// nothing downstream looks at: an inspection command resolves a policy and
    /// prints it, and never executes a rule.
    /// </remarks>
    private static RunOptions PolicyOptionsFrom(ParseResult parse) => new()
    {
        Stage = ValidationStage.PreSubmit,
        Target = StatedTargetFrom(parse),
        Pipeline = PipelineFrom(parse),
        NoLocal = parse.GetValue<bool>("--no-local"),
        AllowLocal = parse.GetValue<bool>("--allow-local"),
        SetOverrides = MultiValued(parse, "--set"),
    };

    /// <summary>
    /// The target of the run, and which halves of it the user actually said.
    /// </summary>
    /// <remarks>
    /// The defaults are <c>any</c> and <c>Development</c>, and that is still
    /// what a rule receives. What travels beside them is whether the user
    /// actually typed each one, because an axis nobody stated must never match
    /// a <c>targets</c> block — otherwise one platform's thresholds apply to
    /// every run that forgot the flag, and the run reports a pass against
    /// limits it was never meant to be judged by.
    /// </remarks>
    private static StatedBuildTarget StatedTargetFrom(ParseResult parse)
    {
        var platform = parse.GetValue<string?>("--platform");
        var configuration = parse.GetValue<string?>("--configuration");

        return new StatedBuildTarget(
            new BuildTarget(platform ?? "any", configuration ?? "Development"),
            PlatformStated: platform is not null,
            ConfigurationStated: configuration is not null);
    }

    /// <summary>
    /// Reads whichever of the two forms was given.
    /// </summary>
    /// <remarks>
    /// Safe to call only after <see cref="RefuseBothPipelineForms"/> has run,
    /// which is why the two live next to each other: reading one form when both
    /// were passed would silently pick a winner.
    /// </remarks>
    private static string? PipelineFrom(ParseResult parse) =>
        parse.GetValue<string?>(CommandLineNames.PipelineOption) ??
        parse.GetValue<string?>(CommandLineNames.DeprecatedPipelineOption);

}
