namespace Preflight.Cli;

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;
using Preflight.Abstractions;
using Preflight.Cli.Commands;
using Preflight.Cli.Interactive;
using Preflight.Core;
using Preflight.Core.Caching;
using Preflight.Core.History;
using Preflight.Core.Plugins;
using Preflight.Core.Policy;

/// <summary>
/// The command surface of <c>preflight</c>.
/// </summary>
/// <remarks>
/// <para>
/// The parse result is inspected here rather than handed to
/// <c>ParseResult.Invoke</c>'s default error path, and that is the single most
/// consequential decision in this file. <c>System.CommandLine</c> returns
/// <b>1</b> when parsing fails, and 1 is the code reserved for "code was
/// rejected". A typo in <c>--stage</c> would therefore page the author of the
/// commit instead of the owner of the tool — silently, forever, and while
/// looking like it works.
/// </para>
/// <para>
/// Everything the CLI refuses rather than guesses at lives here as a validator,
/// so a refusal is a parse error like any other and reaches the same exit code
/// by the same path.
/// </para>
/// </remarks>
public static class PreflightCommandLine
{
    private const string RulesAssemblyName = "Preflight.Rules";

    private const string MeasureCommandArgument = "<command>";

    private const string FormatOption = "--format";

    /// <summary>
    /// What <c>run --format</c> accepts, in the order the refusal names them.
    /// </summary>
    /// <remarks>
    /// A list rather than three literals spread over the option, the validator
    /// and the mapping. The option used to be unconstrained and
    /// anything that was not exactly <c>json</c> silently became console output
    /// with exit 0 — so <c>--format Json</c> handed a screen to a caller that
    /// asked for a machine, and said nothing.
    /// </remarks>
    private static readonly string[] RunFormats = ["console", "json", "sarif"];

    /// <remarks>
    /// The same enum as <see cref="RunFormats"/>, minus the value this command
    /// cannot produce. The restriction lives in the parser rather than in a
    /// second two-valued enum, so that <c>report --format sarif</c> is refused
    /// by name here instead of reaching a handler arm nothing can satisfy.
    /// </remarks>
    private static readonly string[] ReportFormats = ["console", "json"];

    private static readonly string[] GraphFormats = ["text", "dot"];

    /// <summary>
    /// Every command carries it. See
    /// <see cref="PluginLoading"/> for why the flag is not confined to
    /// <c>run</c>.
    /// </summary>
    private const string RulesPathOption = "--rules-path";

    /// <summary>
    /// Parses <paramref name="args"/> and runs the command it names.
    /// </summary>
    /// <returns>The process exit code.</returns>
    public static int Execute(IReadOnlyList<string> args, TextWriter output, TextWriter error) =>
        Execute(args, output, error, parse => Dispatch(parse, output, error));

    /// <summary>
    /// Parses <paramref name="args"/> and hands the result to
    /// <paramref name="dispatch"/>.
    /// </summary>
    /// <param name="dispatch">What runs the parsed command.</param>
    /// <remarks>
    /// The overload exists because the exception boundary below is otherwise
    /// reachable only once a policy file, a graph or a change source is really
    /// loaded. Deleting the boundary to satisfy a coverage number would trade
    /// correct error handling for a percentage — a configuration error escaping
    /// here leaves the process on the runtime's own exit code rather than on 2,
    /// and that difference decides who gets called.
    /// </remarks>
    public static int Execute(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        Func<ParseResult, int> dispatch)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(dispatch);

        var root = BuildRootCommand();

        // No arguments is not an error: it is someone finding out what the tool
        // does. Help on stdout, exit 0 — and an unknown *command* is still exit
        // 2, which is what stops this from degenerating into a binary that
        // prints help for anything and succeeds.
        if (args.Count == 0)
        {
            output.Write(BuildHelpText(root));

            return ExitCode.Success;
        }

        var parse = root.Parse(args, new ParserConfiguration());

        if (parse.Errors.Count > 0)
        {
            foreach (var parseError in parse.Errors)
            {
                error.WriteLine(parseError.Message);
            }

            return ExitCode.ConfigurationError;
        }

        try
        {
            return dispatch(parse);
        }
        catch (ConfigurationLoadException exception)
        {
            error.WriteLine(exception.Message);

            return ExitCode.ForException(exception);
        }
    }

    /// <summary>
    /// Builds the environment a command runs in, from the real machine.
    /// </summary>
    /// <remarks>
    /// Public because a test constructs one with a temporary workspace, a fixed
    /// clock and a writer it can read — which is what makes the commands
    /// assertable without spawning a process.
    /// </remarks>
    public static CommandEnvironment RealEnvironment(TextWriter output, TextWriter error)
    {
        var workspace = new DirectoryInfo(Directory.GetCurrentDirectory());
        var reader = new ProcessEnvironmentReader();

        // Resolved once, here, and handed down. Every command that touches a
        // package needs the same answer, and a second resolution could disagree
        // with the first if a variable changed underneath a long run.
        var installRoot = PipelineInstallRoot.Resolve(reader, workspace);
        var machineStateStore = new MachineStateStore();

        return new()
        {
            WorkspaceRoot = workspace,
            InstallRoot = installRoot,
            InstalledPipelines = new InstalledPipelineReader(installRoot),
            MachineStateStore = machineStateStore,
            MachineState = machineStateStore.Read(installRoot.MachineStatePath),
            PackageArchive = new PackageArchive(),
            Picker = new SpectrePipelinePicker(),
            InstallWriter = new InstallRootWriter(),
            FileSystem = new PhysicalFileSystem(),
            Processes = new ProcessRunner(),
            Children = new ChildProcessLauncher(),
            Environment = reader,
            Console = ConsoleCapabilities.Detect() with { Output = output },
            Error = error,

            // The raw streams, not the writers above. <c>measure</c> propagates the
            // child's bytes, and a TextWriter decodes and re-encodes them.
            RawOutput = System.Console.OpenStandardOutput(),
            RawError = System.Console.OpenStandardError(),

            // Loaded by name rather than through a marker type, exactly as
            // RulesDependencyTests does: adding a type to Preflight.Rules so that
            // this line has something to point at would be production surface
            // serving the CLI's convenience.
            Rules = RuleDiscovery.FromAssemblies(Assembly.Load(new AssemblyName(RulesAssemblyName))),

            // AppContext.BaseDirectory, never Environment.ProcessPath. Under
            // 'dotnet preflight.dll' — which is how the specifications invoke the
            // tool, and how anyone running it from a build output does — the process
            // is the SDK's muxer and its path is the SDK's directory, so the
            // implicit rules/ directory would be probed somewhere nobody can
            // put a plugin.
            ExecutableDirectory = new DirectoryInfo(AppContext.BaseDirectory),
            AssemblyLoader = () => new PluginAssemblyLoader(),
            TimeProvider = TimeProvider.System,
            History = new FileHistoryStore(),
            Cache = new FileRuleCacheStore(),
            Machine = EngineEnvironment.Current,
            WorkspaceWriter = new WorkspaceFileWriter(),
        };
    }

    /// <summary>
    /// Runs the parsed command against <paramref name="environment"/>.
    /// </summary>
    public static int Run(ParseResult parse, CommandEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(environment);

        var name = parse.CommandResult.Command.Name;

        // The name is resolved before any option is read, and that ordering is
        // load-bearing rather than tidy: ParseResult.GetValue throws for a name
        // no symbol declares, so reading run's options while dispatching an
        // unknown command replaces this method's own error with the library's.
        //
        // Synchronous, on purpose. Main returns an int and the whole tool is one
        // run; an async entry point here would add a state machine to every
        // command in exchange for nothing — nothing else is waiting for this
        // thread.

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
        environment = environment with
        {
            ResolvedPackage = ManagesPackages(name)
                ? null
                : PackageResolution.For(environment, PipelineOf(parse), CancellationToken.None),
        };

        environment = environment with
        {
            Rules = PluginLoading.Compose(environment, loader, RulesPathsOf(parse)),
        };

        return name switch
        {
            "run" => RunCommandHandler
                .ExecuteAsync(environment, RunOptionsFrom(parse), CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "rules" => InspectionCommandHandlers
                .RulesAsync(environment, PolicyOptionsFrom(parse), CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "graph" => InspectionCommandHandlers.Graph(
                environment,
                GraphFormatFrom(parse),
                CancellationToken.None),
            // The subcommand's own name, as 'clear' is for cache.
            "workspace" => CreateCommandHandler
                .WorkspaceAsync(environment, CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "rule" => CreateCommandHandler
                .RuleAsync(environment, parse.GetValue<string>("id")!, CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "policy" => CreateCommandHandler
                .PolicyAsync(environment, parse.GetValue<string>("name")!, CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            // The three pipeline subcommands, by their own names, as 'workspace'
            // and 'clear' already are. 'pipeline' alone is a parse error before
            // it reaches here.
            "declare" => PipelineCommandHandler
                .DeclareAsync(environment, parse.GetValue<string?>("name"), CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "use" => PipelineCommandHandler
                .UseAsync(environment, parse.GetValue<string?>("selector"), CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "list" => PipelineCommandHandler
                .ListAsync(environment, CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "pack" => PipelinePackager
                .PackAsync(
                    environment,
                    parse.GetValue<string>("source")!,
                    parse.GetValue<string>("--output")!,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "validate" => PipelineValidator
                .ValidateAsync(environment, parse.GetValue<string>("source")!, CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "install" => PipelineInstaller
                .InstallAsync(
                    environment,
                    parse.GetValue<string>("package")!,
                    parse.GetValue<int?>("--keep"),
                    parse.GetValue<bool>("--no-gc"),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "measure" => MeasureCommandHandler
                .ExecuteAsync(environment, MeasureOptionsFrom(parse), CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "report" => ReportCommandHandler
                .ExecuteAsync(environment, ReportOptionsFrom(parse), CancellationToken.None)
                .GetAwaiter()
                .GetResult(),

            // The subcommand's own name, because that is what the parser
            // resolves to. 'cache' alone is a parse error before it reaches
            // here, so 'clear' is unambiguous.
            "clear" => CacheCommandHandler
                .ClearAsync(environment, PolicyOptionsFrom(parse), CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "explain" => InspectionCommandHandlers
                .ExplainAsync(
                    environment,
                    PolicyOptionsFrom(parse),
                    parse.GetValue<string?>("<rule-id>"),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            _ => NotACommand(name),
        };
    }

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
    /// <remarks>
    /// The subcommands of <c>pipeline</c>, which manage packages rather than
    /// consume one. Resolving a package before <c>install</c> writes it would
    /// refuse the very command that fixes the refusal, and <c>declare</c> exists
    /// because the checkout does not yet say which pipeline it is. <c>pack</c>
    /// and <c>validate</c> work on a source tree named as an argument, so the
    /// package a surrounding checkout happens to resolve to is not theirs and
    /// refusing on it would make an author's tooling depend on which directory
    /// they ran it from.
    /// </remarks>
    private static bool ManagesPackages(string command) =>
        command is "declare" or "use" or "list" or "install" or "pack" or "validate";

    /// <remarks>
    /// Only the commands that declare the option carry one. Reading it off a
    /// command that does not — <c>graph</c>, or a pipeline subcommand — throws
    /// inside the parser rather than returning null.
    /// </remarks>
    private static string? PipelineOf(ParseResult parse) =>
        parse.CommandResult.Command.Options.Any(
            option => string.Equals(option.Name, "--pipeline", StringComparison.Ordinal))
            ? PipelineFrom(parse)
            : null;

    private static string[] RulesPathsOf(ParseResult parse) =>
        parse.CommandResult.Command.Options.Any(
            option => string.Equals(option.Name, RulesPathOption, StringComparison.Ordinal))
            ? MultiValued(parse, RulesPathOption)
            : [];

    /// <remarks>
    /// Unreachable, and excluded rather than covered: the parser rejects an
    /// unknown command before dispatch, so nothing can arrive here that is not
    /// one of the four above. It stays as a throw rather than a silent zero,
    /// because a fifth command added to the surface and forgotten here would
    /// otherwise report success having done nothing.
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
        var command = MultiValued(parse, MeasureCommandArgument);

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
        parse.GetValue<string?>(FormatOption) switch
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
        parse.GetValue<string?>(FormatOption) switch
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

    private static int Dispatch(ParseResult parse, TextWriter output, TextWriter error) =>
        Run(parse, RealEnvironment(output, error));

    private static string BuildHelpText(RootCommand root)
    {
        var writer = new StringWriter();

        writer.WriteLine("preflight — build-readiness validation.");
        writer.WriteLine();
        writer.WriteLine("Commands:");

        foreach (var command in root.Subcommands)
        {
            writer.WriteLine($"  {command.Name,-10} {command.Description}");
        }

        writer.WriteLine();
        writer.WriteLine("Run 'preflight <command> --help' for the options of one command.");

        return writer.ToString();
    }

    private static RootCommand BuildRootCommand()
    {
        var root = new RootCommand("preflight — build-readiness validation.");

        root.Add(BuildRunCommand());
        root.Add(WithRulesPath(WithPolicyOptions(
            new Command("rules", "List the discovered rules, the effective policy and the graph."))));
        root.Add(WithFormat(
            WithRulesPath(new Command("graph", "Print the rule dependency graph.")),
            GraphFormats));

        root.Add(BuildCreateCommand());
        root.Add(BuildPipelineCommand());
        root.Add(BuildMeasureCommand());
        root.Add(BuildReportCommand());
        root.Add(BuildCacheCommand());

        var explain = WithRulesPath(WithPolicyOptions(
            new Command("explain", "Explain how one rule's effective policy was resolved.")));

        // Optional, so that omitting it reaches the handler rather than the
        // parser. Both are exit 2; only one of them can say "try 'preflight
        // rules' to see them", and a message that names the next step is worth
        // more than one that names a grammar.
        explain.Add(new Argument<string?>("<rule-id>")
        {
            Description = "The rule to explain.",
            Arity = ArgumentArity.ZeroOrOne,
        });

        root.Add(explain);

        return root;
    }

    /// <summary>
    /// Adds the options an inspection command needs to resolve a policy.
    /// </summary>
    /// <remarks>
    /// <c>rules</c> and <c>explain</c> print the policy in force, so they have
    /// to be able to say <em>which</em> policy — the same pipeline, the same
    /// overrides, the same overlay decision the run would use. Without these,
    /// <c>explain</c> would answer a question about a configuration nobody
    /// runs.
    /// </remarks>
    private static Command WithPolicyOptions(Command command)
    {
        var pipeline = PipelineOption();
        var production = DeprecatedProductionOption();

        command.Add(pipeline);
        command.Add(production);
        command.Add(new Option<string?>("--platform") { Description = "Target platform." });
        command.Add(new Option<string?>("--configuration") { Description = "Target build configuration." });
        command.Add(new Option<string[]>("--set") { Description = "Override one policy value." });
        command.Add(new Option<bool>("--no-local") { Description = "Ignore preflight.local.json." });
        command.Add(new Option<bool>("--allow-local") { Description = "Apply preflight.local.json even in CI." });

        command.Validators.Add(result => RefuseBothPipelineForms(result, pipeline, production));

        return command;
    }

    /// <summary>
    /// The flag that names which pipeline's policy a command resolves.
    /// </summary>
    /// <remarks>
    /// <c>Docs/design.md 6.2</c>. Named <c>--pipeline</c> since ADR-027; the
    /// value is interpolated into a filename and validated as a label by
    /// <see cref="PolicyResolution"/> before it reaches the disk.
    /// </remarks>
    /// <summary>
    /// The target of the run, and which halves of it the user actually said.
    /// </summary>
    /// <remarks>
    /// The defaults are unchanged — <c>any</c> and <c>Development</c> are still
    /// what a rule receives — but whether they were typed is now recorded,
    /// because a <c>targets</c> block matching a value nobody stated would
    /// apply one platform's thresholds to every run that forgot the flag. See
    /// ADR-030.
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

    private static Option<string?> PipelineOption() =>
        new("--pipeline") { Description = "Named pipeline overlay to apply." };

    /// <summary>
    /// The former name of <see cref="PipelineOption"/>, still accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept because removing it turns every existing CI invocation into a parse
    /// failure on the day the tool is upgraded, which is a migration disguised
    /// as a rename. It is hidden from help so that the documented surface has
    /// one name for one thing, and it emits no warning: there is one line of
    /// stdout per run whose bytes are a contract (<c>Docs/design.md 14</c>) and
    /// one stderr that ADR-015 records CI does not read, so a deprecation
    /// notice would either break the contract or reach nobody.
    /// </para>
    /// <para>
    /// See ADR-027.
    /// </para>
    /// </remarks>
    private static Option<string?> DeprecatedProductionOption() =>
        new("--production") { Description = "Deprecated alias for --pipeline.", Hidden = true };

    /// <summary>
    /// Reads whichever of the two forms was given.
    /// </summary>
    /// <remarks>
    /// Safe to call only after <see cref="RefuseBothPipelineForms"/> has run,
    /// which is why the two live next to each other: reading one form when both
    /// were passed would silently pick a winner.
    /// </remarks>
    private static string? PipelineFrom(ParseResult parse) =>
        parse.GetValue<string?>("--pipeline") ?? parse.GetValue<string?>("--production");

    /// <remarks>
    /// The same refusal, and the same reason, as <c>--no-local</c> with
    /// <c>--allow-local</c>: two spellings of one thing define no precedence
    /// between them, and picking by flag order would decide for the user which
    /// of two different names they meant. Two spellings carrying the
    /// <em>same</em> value is still refused — accepting it would make the rule
    /// depend on the values rather than on the invocation, and a user who fixed
    /// half of a CI script would be told nothing.
    /// </remarks>
    private static void RefuseBothPipelineForms(
        CommandResult result,
        Option<string?> pipeline,
        Option<string?> production)
    {
        if (result.GetValue(pipeline) is not null && result.GetValue(production) is not null)
        {
            result.AddError("--pipeline and --production cannot be used together; --production is the deprecated spelling.");
        }
    }

    /// <summary>
    /// <c>preflight measure --label &lt;label&gt; -- &lt;command&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Both refusals are validators rather than a required option, so that both
    /// reach exit 2 by the same path as every other refusal and carry a message
    /// this file wrote. Both happen before the child is started, which is what
    /// makes 2 and 127 mean different things.
    /// </remarks>
    private static Command BuildMeasureCommand()
    {
        var label = new Option<string?>("--label")
        {
            Description = "What to file this measurement under in the history.",
        };

        var child = new Argument<string[]>(MeasureCommandArgument)
        {
            Description = "The command to time, after '--'.",
            Arity = ArgumentArity.ZeroOrMore,
        };

        var command = WithRulesPath(WithPolicyOptions(
            new Command("measure", "Time a command and record its duration in the history.")));

        command.Add(label);
        command.Add(child);

        command.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(RawToken(result, label)))
            {
                result.AddError("preflight measure requires --label <label>.");
            }

            if (result.GetValue(child) is not { Length: > 0 })
            {
                result.AddError("preflight measure requires a command: preflight measure --label <label> -- <command>.");
            }
        });

        return command;
    }

    /// <summary>
    /// <c>preflight cache clear</c>.
    /// </summary>
    /// <remarks>
    /// A parent command with one subcommand rather than a flat
    /// <c>cache-clear</c>, because the cache is the kind of thing that grows a
    /// second verb — <c>cache stats</c> is the obvious one — and a flat name
    /// would have to be abandoned the first time it did.
    /// </remarks>
    /// <summary>
    /// <c>preflight create workspace</c>.
    /// </summary>
    /// <remarks>
    /// A parent with one subcommand, on the same argument as <c>cache</c>: what
    /// a workspace needs scaffolded is the kind of thing that grows a second
    /// noun, and a flat <c>create-workspace</c> would have to be abandoned the
    /// first time it did.
    ///
    /// No <c>--rules-path</c> and no policy options. ADR-025 puts
    /// <c>--rules-path</c> on every command that discovers rules or resolves a
    /// policy, and this one does neither: it writes a file and exits. See
    /// ADR-028.
    /// </remarks>
    private static Command BuildCreateCommand()
    {
        var workspace = new Command("workspace", "Write a preflight.workspace.json skeleton.");

        var rule = new Command("rule", "Scaffold a plugin project for one rule.")
        {
            new Argument<string>("id") { Description = "The rule id, as in acme.textures.dimension." },
        };

        var policy = new Command("policy", "Write a named pipeline's policy skeleton.")
        {
            new Argument<string>("name") { Description = "The pipeline this policy configures." },
        };

        var command = new Command("create", "Create a file this workspace is missing.")
        {
            workspace,
            rule,
            policy,
        };

        // Named rather than left to the parser, for the reason 'cache' gives:
        // a refusal is worth more when it says what would have worked.
        command.Validators.Add(result =>
        {
            if (!result.Children.OfType<CommandResult>().Any())
            {
                result.AddError("preflight create needs a subcommand: workspace, rule or policy.");
            }
        });

        return command;
    }

    /// <summary>
    /// <c>preflight pipeline declare | use | list | install | pack | validate</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A parent with six subcommands, on the same argument as <c>cache</c> and
    /// <c>create</c>. No policy options on any of them: none configures a run,
    /// and accepting <c>--pipeline</c> would promise it influenced something.
    /// </para>
    /// <para>
    /// <c>--rules-path</c> goes on exactly one of them, and the asymmetry is
    /// ADR-025 read literally: the flag belongs to every command that discovers
    /// rules or resolves a policy. <c>validate</c> does both — it is the command
    /// whose whole job is to load a tree's assemblies and its policy together —
    /// and the other five do neither. See also ADR-035.
    /// </para>
    /// </remarks>
    private static Command BuildPipelineCommand()
    {
        // Optional, so that omitting it reaches the handler rather than the
        // parser. The handler is the only one of the two that can offer what is
        // installed, and it is also the only one that can refuse in a sentence
        // naming the alternative.
        var declare = new Command("declare", "Write which pipeline this checkout is.")
        {
            new Argument<string?>("name")
            {
                Description = "The pipeline this checkout is. Omitted, it is asked for.",
                Arity = ArgumentArity.ZeroOrOne,
            },
        };

        var use = new Command("use", "Pin a pipeline version on this machine.")
        {
            new Argument<string?>("selector")
            {
                Description = "name@version, as in projecta@1.4.0. A bare name asks which version.",
                Arity = ArgumentArity.ZeroOrOne,
            },
        };

        var list = new Command("list", "List the installed pipelines and versions.");

        var install = new Command("install", "Install a pipeline package from a local path.")
        {
            new Argument<string>("package") { Description = "The package to install." },
            new Option<int?>("--keep") { Description = "How many versions of it to retain." },
            new Option<bool>("--no-gc") { Description = "Retain every installed version." },
        };

        var pack = new Command("pack", "Pack a pipeline source tree into a package.")
        {
            new Argument<string>("source") { Description = "The tree to pack." },
            new Option<string>("--output", "-o")
            {
                Description = "Where to write the package. Must not exist.",
                Required = true,
            },
        };

        var validate = WithRulesPath(new Command(
            "validate", "Load a pipeline source tree's policy and rules, and report every error.")
        {
            new Argument<string>("source") { Description = "The tree to validate." },
        });

        var command = new Command("pipeline", "Author, install and select pipeline packages.")
        {
            declare,
            use,
            list,
            install,
            pack,
            validate,
        };

        command.Validators.Add(result =>
        {
            if (!result.Children.OfType<CommandResult>().Any())
            {
                result.AddError(
                    "preflight pipeline needs a subcommand: " +
                    "declare, use, list, install, pack or validate.");
            }
        });

        return command;
    }

    private static Command BuildCacheCommand()
    {
        var clear = WithRulesPath(WithPolicyOptions(new Command("clear", "Empty the incremental cache.")));
        var command = new Command("cache", "Manage the incremental cache.") { clear };

        // Named here rather than left to the parser's own wording, for the same
        // reason as everywhere else: a refusal is worth more when it
        // says what would have worked. Without it, 'preflight cache' reaches
        // dispatch as a command name nothing handles, which is a path that only
        // exists to throw.
        command.Validators.Add(result =>
        {
            if (!result.Children.OfType<CommandResult>().Any())
            {
                result.AddError("preflight cache needs a subcommand: clear.");
            }
        });

        return command;
    }

    /// <summary>
    /// <c>preflight report --since &lt;window&gt;</c>.
    /// </summary>
    private static Command BuildReportCommand()
    {
        var since = new Option<string?>("--since")
        {
            Description = "How far back to look: <number> followed by " +
                string.Join(", ", SinceDuration.AcceptedUnits) + ".",
        };

        var command = WithFormat(
            WithRulesPath(WithPolicyOptions(
                new Command("report", "Summarise the recorded history."))),
            ReportFormats);

        command.Add(since);
        command.Add(new Option<bool>("--no-unicode") { Description = "Use the ASCII glyph variant." });

        command.Validators.Add(result =>
        {
            // One message for both an absent window and a malformed one. The
            // flag is mandatory, and a report over a window nobody chose
            // is a number nobody asked for.
            if (SinceDuration.Parse(RawToken(result, since)) is null)
            {
                result.AddError(
                    "preflight report requires --since <number><unit>, with unit one of " +
                    string.Join(", ", SinceDuration.AcceptedUnits) + " (for example 30d).");
            }
        });

        return command;
    }

    private static Command BuildRunCommand()
    {
        var stage = new Option<string>("--stage")
        {
            Description = "Which stage to run: " + string.Join(", ", StageParser.AcceptedValues) + ".",

            // Stage is the user's intent, not a detail with a sensible
            // default; assuming one runs a set nobody asked for and reports on it.
            Required = true,
        };

        stage.AcceptOnlyFromAmong([.. StageParser.AcceptedValues]);

        var changedFrom = new Option<string?>("--changed-from")
        {
            Description = "Diff against this ref to populate the changed-file set.",
        };

        var noLocal = new Option<bool>("--no-local") { Description = "Ignore preflight.local.json." };
        var allowLocal = new Option<bool>("--allow-local") { Description = "Apply preflight.local.json even in CI." };
        var pipeline = PipelineOption();
        var production = DeprecatedProductionOption();

        var command = new Command("run", "Run the rules of one stage.")
        {
            stage,
            changedFrom,
            new Option<string?>("--platform") { Description = "Target platform." },
            new Option<string?>("--configuration") { Description = "Target build configuration." },
            pipeline,
            production,
            new Option<bool>("--no-skip") { Description = "Execute everything, ignoring gating propagation." },
            new Option<string[]>("--set") { Description = "Override one policy value: <rule-id>:<key>=<value>." },
            new Option<bool>("--fail-on-warning") { Description = "Treat warnings as blocking." },
            new Option<bool>("--no-unicode") { Description = "Use the ASCII glyph variant." },
            new Option<bool>("--no-cache") { Description = "Ignore the incremental cache and re-execute." },
            noLocal,
            allowLocal,
        };

        WithRulesPath(command);
        WithFormat(command, RunFormats);

        command.Validators.Add(result =>
        {
            // The overlay table lists the two as separate rows and defines no
            // precedence between them. Picking one by flag order would decide
            // an integrity question nobody decided.
            if (result.GetValue(noLocal) && result.GetValue(allowLocal))
            {
                result.AddError("--no-local and --allow-local cannot be used together.");
            }

            RefuseBothPipelineForms(result, pipeline, production);

            // Pre-submit rules read ChangedFiles; without a ref they
            // get an empty set, return NotApplicable, and the run goes green
            // having examined nothing.
            //
            // The raw token, not GetValue: a --stage the option already rejected
            // has no converted value, and asking for one throws out of the
            // validator — turning a clean exit 2 into an unhandled exception.
            // A token that parses to no stage simply skips this check, because
            // AcceptOnlyFromAmong has already reported it.
            if (StageParser.Parse(RawToken(result, stage)) == ValidationStage.PreSubmit &&
                string.IsNullOrEmpty(result.GetValue(changedFrom)))
            {
                result.AddError(
                    "--stage pre-submit requires a change source; pass --changed-from <ref>.");
            }
        });

        return command;
    }

    /// <summary>
    /// The text the user typed for <paramref name="option"/>, before
    /// conversion.
    /// </summary>
    /// <remarks>
    /// Conversion can fail, and reading a failed conversion throws. A validator
    /// that throws replaces the exit code it was written to produce with an
    /// unhandled exception, so validators read tokens.
    /// </remarks>
    private static string? RawToken(CommandResult result, Option option) =>
        result.GetResult(option)?.Tokens.Select(token => token.Value).FirstOrDefault();

    /// <summary>
    /// Adds <c>--format</c>, restricted to the values that command implements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AcceptOnlyFromAmong</c>, exactly as <c>--stage</c> uses it, and for
    /// the same reason: a value the tool does not implement is refused with
    /// exit 2 naming the ones it does, rather than accepted and quietly turned
    /// into something else. The option used to be unconstrained, and
    /// every token that was not exactly <c>json</c> — a misspelling, a capital
    /// letter, a format from a later phase — became console output with exit 0.
    /// The caller asked for a machine and got a screen, successfully.
    /// </para>
    /// <para>
    /// This is what replaced the refusal table for flags that were planned but
    /// not yet built. All three it held — <c>--rules-path</c>,
    /// <c>--no-cache</c> and <c>--format sarif</c> — have completed the
    /// journey: refused by name while they did not exist, honoured once they
    /// did.
    /// </para>
    /// </remarks>
    private static Command WithFormat(Command command, string[] accepted)
    {
        var format = new Option<string?>(FormatOption)
        {
            Description = "Output format: " + string.Join(", ", accepted) + ".",
        };

        format.AcceptOnlyFromAmong(accepted);
        command.Add(format);

        return command;
    }

    /// <summary>
    /// Adds <c>--rules-path</c>, which every command carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Repeatable rather than a single value or a separator-delimited list. The
    /// obvious shape is a single path, and a production with two plugin sources
    /// — one shipped with the tool, one belonging to the project — is ordinary
    /// enough that the singular would be worked around within a week. Of the
    /// three ways to express more than one, a repeated option is the only one
    /// that needs no grammar: a delimiter is a parser to write, to test and to
    /// escape, and on Windows every candidate delimiter already occurs in
    /// paths.
    /// </para>
    /// <para>
    /// On every command and not only on <c>run</c>. See
    /// <see cref="PluginLoading"/>: a policy naming a plugin's rule is rejected
    /// with "unknown rule id" by any command that cannot see the plugin, which
    /// is the misleading second error the load order exists to prevent.
    /// </para>
    /// </remarks>
    private static Command WithRulesPath(Command command)
    {
        command.Add(new Option<string[]>(RulesPathOption)
        {
            Description = "Directory of plugin assemblies. Repeat for more than one.",
        });

        return command;
    }
}
