namespace Preflight.Cli;

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;
using Preflight.Abstractions.Model;
using Preflight.Cli.Commands;
using Preflight.Cli.Interactive;
using Preflight.Cli.Model;
using Preflight.Cli.Parsing;
using Preflight.Cli.Pipelines;
using Preflight.Cli.Policy;
using Preflight.Cli.Reporting;
using Preflight.Cli.Storage;
using Preflight.Core;
using Preflight.Core.Caching;
using Preflight.Core.Execution;
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
            Machine = MachineEnvironment.Current,
            WorkspaceWriter = new WorkspaceFileWriter(),
        };
    }

    private static int Dispatch(ParseResult parse, TextWriter output, TextWriter error) =>
        CommandDispatcher.Run(parse, RealEnvironment(output, error));

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
    /// The name selects one layer of the policy precedence chain: descriptor
    /// defaults, then the pipeline's own document and whatever it extends, then
    /// the local overlay, then <c>--set</c>. It is interpolated into a filename
    /// and so is validated as a label by <see cref="PolicyResolution"/> before
    /// it reaches the disk — a name carrying a separator would read a file
    /// outside the workspace.
    ///
    /// It was called <c>--production</c>, which named the game being made
    /// rather than the set of rules and limits that game is checked against.
    /// The flag selects the second.
    /// </remarks>
    private static Option<string?> PipelineOption() =>
        new(CommandLineNames.PipelineOption) { Description = "Named pipeline overlay to apply." };

    /// <summary>
    /// The former name of <see cref="PipelineOption"/>, still accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept because removing it turns every existing CI invocation into a parse
    /// failure on the day the tool is upgraded, which is a migration disguised
    /// as a rename. It is hidden from help so that the surface the tool advertises
    /// has one name for one thing, and it emits no warning: there is one line of
    /// stdout per run whose bytes are fixed — a golden file holds them, and a
    /// consumer diffing two runs is entitled to see nothing move — and one
    /// stderr that continuous integration does not read. A deprecation notice
    /// would therefore either break that guarantee or reach nobody, once per
    /// run, on every machine in the fleet.
    /// </para>
    /// <para>
    /// Passing both spellings is exit 2 naming them, even when they carry the
    /// same value: accepting the agreeing case would make the rule depend on
    /// the values rather than on the invocation, and somebody who had fixed
    /// half of a CI script would be told nothing about the other half.
    /// </para>
    /// </remarks>
    private static Option<string?> DeprecatedProductionOption() =>
        new(CommandLineNames.DeprecatedPipelineOption)
        {
            Description = "Deprecated alias for --pipeline.",
            Hidden = true,
        };

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

        var child = new Argument<string[]>(CommandLineNames.MeasureCommandArgument)
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
    /// <c>preflight create workspace</c>.
    /// </summary>
    /// <remarks>
    /// A parent with one subcommand, on the same argument as <c>cache</c>: what
    /// a workspace needs scaffolded is the kind of thing that grows a second
    /// noun, and a flat <c>create-workspace</c> would have to be abandoned the
    /// first time it did.
    ///
    /// No <c>--rules-path</c> and no policy options. The flag belongs to every
    /// command that discovers a rule or resolves a policy, and this one does
    /// neither: it writes a file and exits. Offering the flag would promise it
    /// changed something.
    ///
    /// It is also the one command in the tool that writes inside the workspace
    /// being validated, which it does through a writer of its own that refuses
    /// to replace an existing file. A rule never repairs what it finds; a
    /// person typing <c>create</c> is applying the correction themselves, which
    /// is the other half of the same rule rather than an exception to it.
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
    /// the general rule applied literally: the flag belongs to every command
    /// that discovers a rule or resolves a policy. <c>validate</c> does both —
    /// it is the command whose whole job is to load a tree's assemblies and its
    /// policy together and report every error in one pass — and the other five
    /// do neither.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The subcommands of <c>pipeline</c>, by name.
    /// </summary>
    /// <remarks>
    /// Read off the command this file builds rather than written out a second
    /// time, so the two cannot disagree. <see cref="CommandDispatcher"/> asks
    /// this to decide which invocations resolve an installed package, and a
    /// subcommand added below is in the answer without anything else being
    /// edited.
    /// </remarks>
    internal static IReadOnlySet<string> PackageManagingCommands { get; } =
        BuildPipelineCommand().Subcommands
            .Select(subcommand => subcommand.Name)
            .ToHashSet(StringComparer.Ordinal);

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

        var command = new Command(
            CommandLineNames.PipelineCommand, "Author, install and select pipeline packages.")
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

    /// <summary>
    /// <c>preflight cache clear</c>.
    /// </summary>
    /// <remarks>
    /// A parent command with one subcommand rather than a flat
    /// <c>cache-clear</c>, because the cache is the kind of thing that grows a
    /// second verb — <c>cache stats</c> is the obvious one — and a flat name
    /// would have to be abandoned the first time it did.
    /// </remarks>
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
        var format = new Option<string?>(CommandLineNames.FormatOption)
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
        command.Add(new Option<string[]>(CommandLineNames.RulesPathOption)
        {
            Description = "Directory of plugin assemblies. Repeat for more than one.",
        });

        return command;
    }
}
