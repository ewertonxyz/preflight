namespace Preflight.Core.Tests.Execution;

using NSubstitute;
using Preflight.Abstractions;
using Preflight.Core;
using Preflight.Core.Policy;

/// <summary>
/// Assembles a <see cref="RunRequest"/> with inert defaults.
/// </summary>
/// <remarks>
/// <see cref="RunRequest"/> has a dozen required members, and without this every
/// executor test would open with fifteen lines of arrange in which the one line
/// that matters is invisible.
///
/// <see cref="IFileSystem"/> and <see cref="IProcessRunner"/> are substituted
/// and left unconfigured on purpose: the executor only ever hands them to a
/// rule, so an unconfigured substitute is exactly right, and a rule that
/// touched one would be doing something these tests do not ask for.
/// </remarks>
internal static class RunFixture
{
    public static readonly Guid FixedRunId = new("11111111-2222-3333-4444-555555555555");

    public static RunRequest For(IReadOnlyList<FakeRule> rules, EffectivePolicy policy) => new()
    {
        Rules = rules,
        Policy = policy,
        Stage = ValidationStage.PreSubmit,
        Target = new BuildTarget("x64", "Debug"),
        WorkspaceRoot = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "preflight-tests")),
        ChangedFiles = [],
        FileSystem = Substitute.For<IFileSystem>(),
        Processes = Substitute.For<IProcessRunner>(),
        PolicyChain = ["preflight.base.json", "preflight.atlas.json"],
        Pipeline = "atlas",
        RunId = FixedRunId,
    };

    public static IReadOnlyList<RuleDescriptor> DescriptorsOf(IEnumerable<FakeRule> rules) =>
        [.. rules.Select(rule => rule.Descriptor)];
}
