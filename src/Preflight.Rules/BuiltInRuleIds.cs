namespace Preflight.Rules;

using Preflight.Abstractions.Rules;

/// <summary>
/// The ids of the rules that ship with the tool.
/// </summary>
/// <remarks>
/// Declared once because they appear in three places that must agree: the
/// descriptor of the rule itself, the <c>DependsOn</c> list of another rule,
/// and the documentation. A typo in a dependency id is not a compile error — it
/// is a "no rule with that id" reported at graph-build time, which is hard to
/// tell apart from a rule the policy disabled.
/// </remarks>
public static class BuiltInRuleIds
{
    public static readonly RuleId Toolchain = new("core.workspace.toolchain");

    public static readonly RuleId Dependencies = new("core.workspace.dependencies");

    public static readonly RuleId ForbiddenPaths = new("core.presubmit.forbidden-paths");

    public static readonly RuleId LargeFile = new("core.presubmit.large-file");

    public static readonly RuleId BuildConfiguration = new("core.build.configuration");

    public static readonly RuleId CompileProbe = new("core.build.compile-probe");
}
