namespace Preflight.Cli;

/// <summary>
/// The names the command surface declares and the dispatcher reads back.
/// </summary>
/// <remarks>
/// <para>
/// One spelling of each, because two types depend on every one of them and
/// they depend on it for opposite reasons: <see cref="PreflightCommandLine"/>
/// builds the option, <see cref="CommandDispatcher"/> asks the parse result
/// for its value. A literal in each place compiles perfectly while they
/// disagree, and the symptom is a flag that parses and is then never read —
/// which looks exactly like a flag that does nothing.
/// </para>
/// <para>
/// Internal rather than public. These are the tool's vocabulary, not a
/// contract anything outside this assembly consumes, and a test that asserted
/// on them would be asserting on a string rather than on what the tool does
/// with it.
/// </para>
/// </remarks>
internal static class CommandLineNames
{
    /// <summary>
    /// The parent of the subcommands that manage packages.
    /// </summary>
    /// <remarks>
    /// <c>CommandDispatcher</c> recognises a package command by its parent, and
    /// <c>PreflightCommandLine</c> is what gives it that parent.
    /// </remarks>
    public const string PipelineCommand = "pipeline";

    /// <summary>The argument holding everything after <c>measure --label x --</c>.</summary>
    public const string MeasureCommandArgument = "<command>";

    /// <summary>The output format flag, on the three commands that render.</summary>
    public const string FormatOption = "--format";

    /// <summary>
    /// The directory of plugin assemblies, repeatable, on every command that
    /// discovers a rule or resolves a policy.
    /// </summary>
    public const string RulesPathOption = "--rules-path";

    /// <summary>The flag that names which pipeline's policy a command resolves.</summary>
    public const string PipelineOption = "--pipeline";

    /// <summary>Its former spelling, still accepted and hidden from help.</summary>
    public const string DeprecatedPipelineOption = "--production";
}
