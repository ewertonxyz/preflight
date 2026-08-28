namespace Preflight.Core.Tests.Policy;

using Preflight.Abstractions;
using Preflight.Core.Policy;

/// <summary>
/// Fixes <see cref="PolicySetOverride.ToNode"/>: an already-typed <c>--set</c>
/// value converts into the same <see cref="PolicyNode"/> shape a JSON layer
/// produces, so it enters <see cref="PolicyNode.Merge"/> through the identical
/// code path rather than a second, divergent merge implementation.
/// </summary>
/// <remarks>
/// policy precedence. Parsing the <c>--set</c> command-line flag
/// itself (the <c>:</c> separator, the greedy id-prefix form) is out of scope
/// here — that is the CLI, <c>Cli.Tests</c>. This only covers what the policy layer
/// receives: a rule id (or none, for a root key), a dotted path, and an
/// already-typed value.
/// </remarks>
public sealed class SetOverlayTests
{
    [Fact]
    public void ToNode_WithARuleScopedPath_PlacesTheLeafAtTheNestedSettingsPath()
    {
        var overlay = new PolicySetOverride
        {
            RuleId = new RuleId("core.presubmit.large-file"),
            Path = "settings.maxBytes",
            TypedValue = 1024L,
        };

        var node = overlay.ToNode();

        node.TryGetPath(["rules", "core.presubmit.large-file", "settings", "maxBytes"], out var leaf).ShouldBeTrue();
        ((PolicyNode.Leaf)leaf!).Value.Value.ShouldBe(1024L);
    }

    [Fact]
    public void ToNode_WithARootScopedPath_PlacesTheLeafAtTheRootKey()
    {
        var overlay = new PolicySetOverride
        {
            RuleId = null,
            Path = "maxDegreeOfParallelism",
            TypedValue = 4L,
        };

        var node = overlay.ToNode();

        node.TryGetPath("maxDegreeOfParallelism", out var leaf).ShouldBeTrue();
        ((PolicyNode.Leaf)leaf!).Value.Value.ShouldBe(4L);
    }

    [Fact]
    public void ToNode_Origin_IsACommandLineOriginNotAFileOrigin()
    {
        var overlay = new PolicySetOverride
        {
            RuleId = new RuleId("core.presubmit.large-file"),
            Path = "blocking",
            TypedValue = false,
        };

        var node = overlay.ToNode();
        node.TryGetPath(["rules", "core.presubmit.large-file", "blocking"], out var leaf).ShouldBeTrue();

        ((PolicyNode.Leaf)leaf!).Value.Origin.ShouldBeOfType<PolicyOrigin.FromCommandLine>();
    }

    [Fact]
    public void ToNode_WithAnArrayTypedValue_ProducesAStringArrayLeaf()
    {
        string[] extensions = ["a", "b", "c"];

        var overlay = new PolicySetOverride
        {
            RuleId = new RuleId("core.presubmit.large-file"),
            Path = "settings.allowExtensions",
            TypedValue = extensions,
        };

        var node = overlay.ToNode();
        node.TryGetPath(["rules", "core.presubmit.large-file", "settings", "allowExtensions"], out var leaf).ShouldBeTrue();

        ((PolicyNode.Leaf)leaf!).Value.Value.ShouldBe(extensions);
    }
}
