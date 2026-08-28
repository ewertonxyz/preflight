namespace Preflight.Cli.Tests;

using Preflight.Abstractions.Model;
using Preflight.Core;

/// <summary>
/// Fixes the command surface and the refusals it makes.
/// </summary>
/// <remarks>
/// The exit code is the assertion in almost every test here, because the exit
/// code is what a pipeline reads. The exit-code contract makes 2 mean "the tool's owner
/// broke something" and 1 mean "the commit's author did" — a defect that
/// returns the wrong one routes an incident to the wrong person while looking
/// like it works.
/// </remarks>
public sealed class PreflightCommandLineTests
{
    private sealed record Invocation(int ExitCode, string Output, string Error);

    private static Invocation Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = PreflightCommandLine.Execute(args, output, error);

        return new Invocation(exitCode, output.ToString(), error.ToString());
    }

    /// <summary>
    /// Nobody typing <c>preflight</c> alone made a mistake — they are finding
    /// out what it does.
    /// </summary>
    /// <remarks>
    /// The exact set, in order, rather than a containment check per name. The
    /// first version of this test asserted four <c>ShouldContain</c>s and would
    /// have stayed green with two commands missing — which is what it looked
    /// like when the history added two and nothing failed. A set comparison is the
    /// only shape of this assertion that notices either direction.
    /// </remarks>
    [Fact]
    public void Execute_WithNoArguments_PrintsEveryCommandAndOnlyThose()
    {
        var invocation = Run();

        invocation.ExitCode.ShouldBe(0);
        CommandsIn(invocation.Output)
            .ShouldBe([
                "run", "rules", "graph", "create", "pipeline", "measure", "report", "cache", "explain",
            ]);
    }

    /// <summary>
    /// The command names out of the help text, in the order it lists them.
    /// </summary>
    private static IReadOnlyList<string> CommandsIn(string helpText) =>
    [
        .. helpText
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("Commands:", StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => line.StartsWith("  ", StringComparison.Ordinal))
            .Select(line => line.Trim().Split(' ')[0]),
    ];

    /// <remarks>
    /// The test that keeps the one above from being vacuous. Without it, a
    /// binary that printed help for any input and exited 0 would satisfy both.
    /// </remarks>
    [Fact]
    public void Execute_WithAnUnknownCommand_IsAConfigurationError()
    {
        Run("nonsense").ExitCode.ShouldBe(2);
    }

    /// <summary>
    /// The most likely defect of this whole block, and the least visible.
    /// </summary>
    /// <remarks>
    /// <c>System.CommandLine</c> returns 1 when parsing fails, and 1 is the code
    /// the exit-code contract reserves for rejected code. Left at the library default, a
    /// typo in a CI yaml would page the author of the commit instead of the
    /// owner of the tool — and the run would look like it worked.
    /// </remarks>
    [Fact]
    public void Execute_WithAMisspelledStage_IsTwoAndNotOne()
    {
        Run("run", "--stage", "workspce").ExitCode.ShouldBe(2);
    }

    [Fact]
    public void Execute_WithAStageMissingItsHyphen_IsAConfigurationError()
    {
        Run("run", "--stage", "presubmit").ExitCode.ShouldBe(2);
    }

    /// <remarks>Stage is intent, and intent has no default.</remarks>
    [Fact]
    public void Execute_WithoutAStage_IsAConfigurationError()
    {
        var invocation = Run("run");

        invocation.ExitCode.ShouldBe(2);
        invocation.Error.ShouldContain("--stage");
    }

    [Fact]
    public void Execute_WithAnUnknownFlag_IsAConfigurationError()
    {
        Run("run", "--stage", "workspace", "--nonsense").ExitCode.ShouldBe(2);
    }

    /// <remarks>
    /// The local-overlay rule lists the two as separate rows and defines no precedence
    /// between them. Honouring one by flag order would settle an integrity
    /// question that nobody settled.
    /// </remarks>
    [Fact]
    public void Execute_WithBothNoLocalAndAllowLocal_IsAConfigurationError()
    {
        var invocation = Run("run", "--stage", "workspace", "--no-local", "--allow-local");

        invocation.ExitCode.ShouldBe(2);
        invocation.Error.ShouldContain("--no-local");
        invocation.Error.ShouldContain("--allow-local");
    }

    /// <remarks>
    /// Pre-submit rules read <c>ChangedFiles</c>; with no ref they get
    /// an empty set, return <c>NotApplicable</c>, and the step goes green having
    /// examined nothing. Removing <c>--changed-from</c> from a CI yaml is a
    /// one-character edit that would otherwise be undetectable.
    /// </remarks>
    [Fact]
    public void Execute_WithPreSubmitAndNoChangedFrom_IsAConfigurationError()
    {
        var invocation = Run("run", "--stage", "pre-submit");

        invocation.ExitCode.ShouldBe(2);
        invocation.Error.ShouldContain("--changed-from");
    }

    /// <remarks>
    /// The change-source refusal happens during parsing, so an invocation that
    /// gets past it produces no parse error — which is all this asserts.
    /// Whether the run then succeeds is <c>CommandEndToEndTests</c>' question.
    /// </remarks>
    [Fact]
    public void Execute_WithPreSubmitAndAChangedFrom_PassesValidation()
    {
        var error = new StringWriter();

        PreflightCommandLine.Execute(
            ["run", "--stage", "pre-submit", "--changed-from", "HEAD~1"],
            new StringWriter(),
            error,
            _ => 0)
            .ShouldBe(0);

        error.ToString().ShouldBeEmpty();
    }

    /// <summary>
    /// <c>--rules-path</c> parses, and is not mistaken for an unknown flag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method replaced the future-phase table that used to hold
    /// <c>--rules-path</c> alongside <c>--no-cache</c>. All three flags it ever
    /// held — those two and <c>--format sarif</c> — have now made the same
    /// journey: documented in the command surface before they existed, refused by name
    /// and phase while they did not, honoured once the phase landed. The table
    /// is empty and the method that built it is deleted; what stands in its
    /// place is the <c>AcceptOnlyFromAmong</c> on <c>--format</c> that
    /// <see cref="Execute_WithAnUnknownFormat_IsTwoAndNamesTheAcceptedValues"/>
    /// and its control below assert.
    /// </para>
    /// <para>
    /// Exit 2 here would mean the parser rejected the flag. The temporary
    /// directory is empty, so the interesting part is the parse: what the flag
    /// then does to the rule set is asserted where the loader is.
    /// </para>
    /// </remarks>
    [Fact]
    public void Execute_WithRulesPath_IsAcceptedRatherThanRefused()
    {
        var empty = Directory.CreateTempSubdirectory("preflight-rules-path-");

        try
        {
            Run("run", "--stage", "workspace", "--rules-path", empty.FullName).ExitCode.ShouldNotBe(2);
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A <c>--rules-path</c> that is not there is exit 2, naming it.
    /// </summary>
    /// <remarks>
    /// The sharpest case for refusing rather than guessing. Accepting the
    /// path and finding nothing would finish a run without the rules the
    /// production declared and report success — the false green of principle 7,
    /// reached by being helpful.
    /// </remarks>
    [Fact]
    public void Execute_WithANonexistentRulesPath_IsTwoAndNamesThePath()
    {
        var invocation = Run("run", "--stage", "workspace", "--rules-path", "no-such-directory");

        invocation.ExitCode.ShouldBe(2);
        invocation.Error.ShouldContain("no-such-directory");
    }

    /// <remarks>
    /// Every command carries the flag, not only <c>run</c>. A policy naming a
    /// plugin's rule is rejected with "unknown rule id" by any command that
    /// cannot see the plugin, which is the misleading second error the early
    /// load exists to prevent — and six commands reaching it
    /// through another door would be the same defect.
    /// </remarks>
    [Theory]
    [InlineData("rules")]
    [InlineData("graph")]
    [InlineData("explain")]
    [InlineData("measure")]
    [InlineData("report")]
    [InlineData("cache", "clear")]

    // ADR-025 read literally: the flag belongs to every command that discovers
    // rules or resolves a policy, and 'pipeline validate' is the one subcommand
    // of 'pipeline' that does both.
    [InlineData("pipeline", "validate")]
    public void Execute_ForEveryCommand_RulesPathIsARecognisedOption(string command, string? subcommand = null)
    {
        var empty = Directory.CreateTempSubdirectory("preflight-rules-path-");

        try
        {
            string[] args = subcommand is null
                ? [command, "--rules-path", empty.FullName]
                : [command, subcommand, "--rules-path", empty.FullName];

            // Not the exit code: half of these refuse for a reason of their own
            // — explain wants a rule id, report wants a window — and a code
            // would confuse "the option is unknown" with "the invocation is
            // incomplete". An unrecognised option is reported by name, so its
            // absence from the error stream is the assertion.
            Run(args).Error.ShouldNotContain("rules-path");
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }

    /// <summary>
    /// <c>--no-cache</c> parses, and is not mistaken for an unknown flag.
    /// </summary>
    /// <remarks>
    /// Exit 2 here would mean the parser rejected it; anything else means it was
    /// understood. The run itself is a workspace with nothing in it, so the
    /// interesting part is the parse rather than the verdict — what the flag
    /// then does to the engine is asserted where the engine is, in
    /// <c>RunCacheTests</c>.
    /// </remarks>
    [Fact]
    public void Execute_WithNoCache_IsAcceptedRatherThanRefused() =>
        Run("run", "--stage", "workspace", "--no-cache").ExitCode.ShouldNotBe(2);

    /// <summary>
    /// Both spellings of the pipeline flag are declared on every command that
    /// resolves a policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The matrix is the test. The flag is declared in four separate places —
    /// <c>run</c>, and the inspection commands through
    /// <c>WithPolicyOptions</c> — and forgetting one of them is silent: the
    /// command still parses everything else, and the missing option surfaces as
    /// an unrecognised argument in somebody's CI script.
    /// </para>
    /// <para>
    /// Asserted on the message rather than on the exit code, because both
    /// outcomes here are 2 and only one of them is the defect: naming a
    /// pipeline whose file is absent is a refusal this tool owes the user, so
    /// what distinguishes an undeclared option is that the parser names the
    /// flag back.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("--pipeline")]
    [InlineData("--production")]
    public void Execute_WithEitherSpellingOfThePipelineFlag_IsDeclaredByEveryCommandThatResolvesAPolicy(string flag)
    {
        // Every command that resolves a policy, and only those. `graph` is
        // absent on purpose: it prints the dependency graph, which is derived
        // from the rule descriptors and not from any policy file.
        string[][] invocations =
        [
            ["run", "--stage", "workspace", flag, "atlas"],
            ["rules", flag, "atlas"],
            ["explain", "core.workspace.toolchain", flag, "atlas"],
            ["measure", "--label", "x", flag, "atlas", "--", "preflight-no-such-child"],
            ["report", "--since", "7d", flag, "atlas"],
            ["cache", "clear", flag, "atlas"],
        ];

        foreach (var arguments in invocations)
        {
            Run(arguments).Error.ShouldNotContain(
                "Unrecognized",
                Case.Sensitive,
                $"'{arguments[0]}' does not declare {flag}.");
        }
    }

    /// <summary>
    /// Every command that resolves a policy can name the target it resolves for.
    /// </summary>
    /// <remarks>
    /// Before the target layer these five resolved a policy against a fixed
    /// <c>any/Development</c>, which was harmless while no policy could vary by
    /// target. It stopped being harmless the moment one could: without the
    /// flags, <c>explain</c> would print a policy no run uses — the exact thing
    /// the note on <c>WithPolicyOptions</c> says those options exist to
    /// prevent. The matrix guards the sixth command somebody adds. See ADR-030.
    /// </remarks>
    [Theory]
    [InlineData("--platform")]
    [InlineData("--configuration")]
    public void Execute_WithATargetFlag_IsDeclaredByEveryCommandThatResolvesAPolicy(string flag)
    {
        string[][] invocations =
        [
            ["run", "--stage", "workspace", flag, "ps5"],
            ["rules", flag, "ps5"],
            ["explain", "core.workspace.toolchain", flag, "ps5"],
            // A child that cannot start, on purpose: with a target the policy
            // now resolves cleanly, so measure would really launch what it is
            // given — and an interactive shell waits forever. 127 is the
            // documented answer for "the command does not exist", and it is not
            // 2, which is what this asserts.
            ["measure", "--label", "x", flag, "ps5", "--", "preflight-no-such-child"],
            ["report", "--since", "7d", flag, "ps5"],
            ["cache", "clear", flag, "ps5"],
        ];

        foreach (var arguments in invocations)
        {
            Run(arguments).Error.ShouldNotContain(
                "Unrecognized",
                Case.Sensitive,
                $"'{arguments[0]}' does not declare {flag}.");
        }
    }

    /// <summary>
    /// <c>preflight create</c> without a subcommand is a refusal that names one.
    /// </summary>
    /// <remarks>
    /// The same shape as <c>cache</c>, and guarded separately because a second
    /// parent command re-opens the hole this file's own remarks describe:
    /// left to the parser, a malformed invocation exits 1, which is the code
    /// that means "the code was rejected".
    /// </remarks>
    [Fact]
    public void Execute_WithCreateAndNoSubcommand_IsTwoAndNamesTheSubcommand()
    {
        var invocation = Run("create");

        invocation.ExitCode.ShouldBe(2);
        invocation.Error.ShouldContain("workspace");
        invocation.Error.ShouldContain("rule");
        invocation.Error.ShouldContain("policy");
    }

    /// <remarks>
    /// The example used to be <c>create pipeline</c>, which stayed green and
    /// started reading as though <c>create pipeline</c> were a refusal somebody
    /// intended — in the same change that added a root command called
    /// <c>pipeline</c>. A name nothing in the tool answers to is the honest
    /// sample.
    /// </remarks>
    [Fact]
    public void Execute_WithAnUnknownCreateSubcommand_IsTwo() =>
        Run("create", "widget").ExitCode.ShouldBe(2);

    [Fact]
    public void Execute_WithPipelineAndNoSubcommand_IsTwoAndNamesTheSubcommands()
    {
        var invocation = Run("pipeline");

        invocation.ExitCode.ShouldBe(2);
        invocation.Error.ShouldContain("declare");
        invocation.Error.ShouldContain("use");
        invocation.Error.ShouldContain("list");
        invocation.Error.ShouldContain("install");
        invocation.Error.ShouldContain("pack");
        invocation.Error.ShouldContain("validate");
    }

    /// <remarks>
    /// The other five manage or produce a package and neither discover a rule
    /// nor resolve a policy, so ADR-025 denies them the flag. Asserted from this
    /// side too, because a matrix that only ever checks presence would stay
    /// green while the flag spread to every command that happened to be
    /// convenient.
    /// </remarks>
    [Theory]
    [InlineData("pipeline|declare|projecta")]
    [InlineData("pipeline|use|projecta@1.4.0")]
    [InlineData("pipeline|list")]
    [InlineData("pipeline|install|package.zip")]
    [InlineData("pipeline|pack|source|-o|out.zip")]
    [InlineData("create|workspace")]
    [InlineData("create|rule|acme.textures.dimension")]
    [InlineData("create|policy|projecta")]
    public void Execute_ForACommandThatDiscoversNoRules_RulesPathIsNotAnOption(string arguments)
    {
        // Asked of the parsed command rather than of an error message. A
        // refusal here would only prove that the invocation was wrong in some
        // way, and "some way" is the assertion that stays green after the
        // option quietly arrives.
        var declared = true;

        PreflightCommandLine.Execute(
            arguments.Split('|'),
            TextWriter.Null,
            TextWriter.Null,
            parse =>
            {
                declared = parse.CommandResult.Command.Options.Any(option =>
                    string.Equals(option.Name, "--rules-path", StringComparison.Ordinal));

                return 0;
            });

        declared.ShouldBeFalse();
    }

    /// <summary>
    /// <c>preflight cache</c> without a subcommand is a refusal that names one.
    /// </summary>
    /// <remarks>
    /// The same reason as everywhere else: a refusal is worth more when it says what would
    /// have worked. Left to the parser, the message names no subcommand; left to
    /// dispatch, it reaches a throw that exists only because nothing should get
    /// there.
    /// </remarks>
    [Fact]
    public void Execute_WithCacheAndNoSubcommand_IsTwoAndNamesTheSubcommand()
    {
        var invocation = Run("cache");

        invocation.ExitCode.ShouldBe(2);
        invocation.Error.ShouldContain("clear");
    }

    /// <summary>
    /// <c>--format sarif</c> parses, and is not mistaken for a future phase.
    /// </summary>
    /// <remarks>
    /// The third and last flag to make the journey of the command surface's phase column,
    /// after <c>--rules-path</c> and <c>--no-cache</c>. <c>ShouldNotBe(2)</c>
    /// rather than <c>ShouldBe(0)</c> for the same reason as those two: the
    /// workspace this runs against is empty, so what is interesting here is the
    /// parse and not the verdict.
    /// </remarks>
    [Fact]
    public void Execute_WithFormatSarif_IsAcceptedRatherThanRefused() =>
        Run("run", "--stage", "workspace", "--format", "sarif").ExitCode.ShouldNotBe(2);

    /// <summary>
    /// A <c>--format</c> the tool does not implement is exit 2, in all three
    /// commands that carry the flag.
    /// </summary>
    /// <remarks>
    /// The defect this closed. <c>--format</c> was an unconstrained option
    /// and everything that was not exactly <c>json</c> became console output
    /// with exit 0 — so a pipeline that asked for a machine got a screen, and
    /// the tool said it had succeeded. The <c>Json</c> case is the sharpest of
    /// the five, because the comparison is ordinal and a capital letter is the
    /// misspelling somebody actually makes.
    /// </remarks>
    [Theory]
    [InlineData("run|--stage|workspace|--format|bogus")]
    [InlineData("run|--stage|workspace|--format|Json")]
    [InlineData("run|--stage|workspace|--format|SARIF")]
    [InlineData("graph|--format|bogus")]
    [InlineData("report|--since|30d|--format|bogus")]
    public void Execute_WithAnUnknownFormat_IsTwoAndNamesTheAcceptedValues(string arguments)
    {
        var invocation = Run(arguments.Split('|'));

        invocation.ExitCode.ShouldBe(2);
        invocation.Error.ShouldNotBeEmpty();
    }

    /// <summary>
    /// Every value the command surface documents is accepted by the command that
    /// documents it.
    /// </summary>
    /// <remarks>
    /// The negative control of the test above, and it is not optional: an
    /// <c>AcceptOnlyFromAmong</c> given the wrong set refuses a documented value
    /// and every assertion about refusals stays green. Same shape as
    /// <see cref="Execute_WithNoCache_IsAcceptedRatherThanRefused"/>, and
    /// <c>ShouldNotBe(2)</c> for the same reason.
    /// </remarks>
    [Theory]
    [InlineData("run|--stage|workspace|--format|console")]
    [InlineData("run|--stage|workspace|--format|json")]
    [InlineData("run|--stage|workspace|--format|sarif")]
    [InlineData("graph|--format|text")]
    [InlineData("graph|--format|dot")]
    [InlineData("report|--since|30d|--format|console")]
    [InlineData("report|--since|30d|--format|json")]
    public void Execute_ForEveryCommandAndFormat_AcceptsTheDocumentedValues(string arguments) =>
        Run(arguments.Split('|')).ExitCode.ShouldNotBe(2);

    /// <summary>
    /// A configuration error raised while running a command is exit 2.
    /// </summary>
    /// <remarks>
    /// The boundary that turns every load-time failure — an invalid policy, a
    /// cycle in the graph, a ref that does not resolve — into the one code
    /// the exit-code contract reserves for "the tool's owner broke something". With the
    /// real dispatch it is reached only once a policy file or a change source
    /// is actually loaded, which is block 5E; injecting the dispatch is what
    /// makes it assertable before then rather than after the first user hits it.
    /// </remarks>
    [Fact]
    public void Execute_WhenTheCommandRaisesAConfigurationError_IsTwoAndReportsTheMessage()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = PreflightCommandLine.Execute(
            ["run", "--stage", "workspace"],
            output,
            error,
            _ => throw new RuleDiscoveryException("nothing to validate against"));

        exitCode.ShouldBe(2);
        error.ToString().ShouldContain("nothing to validate against");
    }

    [Fact]
    public void Execute_WhenTheCommandSucceeds_ReturnsItsExitCode()
    {
        PreflightCommandLine.Execute(
            ["run", "--stage", "workspace"],
            new StringWriter(),
            new StringWriter(),
            _ => 0)
            .ShouldBe(0);
    }

    [Theory]
    [InlineData("workspace", ValidationStage.Workspace)]
    [InlineData("pre-submit", ValidationStage.PreSubmit)]
    [InlineData("build-readiness", ValidationStage.BuildReadiness)]
    public void StageParser_AcceptsTheThreeDocumentedSpellings(string argument, ValidationStage expected)
    {
        StageParser.Parse(argument).ShouldBe(expected);
    }

    [Theory]
    [InlineData("presubmit")]
    [InlineData("Workspace")]
    [InlineData("")]
    [InlineData(null)]
    public void StageParser_RejectsAnythingElse(string? argument)
    {
        StageParser.Parse(argument).ShouldBeNull();
    }

    /// <remarks>
    /// Round-trip, so the spelling the parser accepts and the spelling the help
    /// text and error messages print cannot drift apart.
    /// </remarks>
    [Theory]
    [InlineData(ValidationStage.Workspace)]
    [InlineData(ValidationStage.PreSubmit)]
    [InlineData(ValidationStage.BuildReadiness)]
    public void StageParser_RoundTrips(ValidationStage stage)
    {
        StageParser.Parse(StageParser.ToArgument(stage)).ShouldBe(stage);
    }

    [Fact]
    public void StageParser_ToArgument_WithAValueOutsideTheEnum_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => StageParser.ToArgument((ValidationStage)99));
    }

    [Fact]
    public void StageParser_AcceptedValues_MatchesTheThreeSpellings()
    {
        StageParser.AcceptedValues.ShouldBe(["workspace", "pre-submit", "build-readiness"]);
    }
}
