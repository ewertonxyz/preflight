namespace Preflight.Core.Tests.Policy;

using Preflight.Abstractions;
using Preflight.Core.Policy;

/// <summary>
/// Fixes where the <c>targets</c> layer sits in the precedence chain and what
/// it does when several of its keys apply at once.
/// </summary>
/// <remarks>
/// One rule and one setting throughout, because what is under test is the
/// order of the layers rather than the merge itself — the merge has its own
/// tests, and repeating them here with a target block on top would only make a
/// failure harder to read. See ADR-030.
/// </remarks>
public sealed class EffectivePolicyTargetLayerTests
{
    private static readonly RuleDescriptor LargeFile = new()
    {
        Id = new RuleId("core.presubmit.large-file"),
        DisplayName = "Large file",
        Stage = ValidationStage.PreSubmit,
    };

    private static StatedBuildTarget Stated(string platform, string configuration) =>
        new(new BuildTarget(platform, configuration), PlatformStated: true, ConfigurationStated: true);

    private static PolicyDocument Document(string json) => PolicyDocument.Parse(json, "projectc.json");

    private static EffectivePolicy Build(
        string pipelineJson, StatedBuildTarget target, PolicyDocument? local = null) =>
        EffectivePolicy.Build([LargeFile], Document(pipelineJson), local, [], target);

    private static long MaxBytes(EffectivePolicy policy) =>
        policy.ReaderFor(LargeFile.Id).GetValue<long>("maxBytes", 0);

    /// <summary>
    /// Every key that applies applies, least specific first.
    /// </summary>
    /// <remarks>
    /// Not "the most specific wins and the rest are discarded": that would make
    /// <c>ps5|Shipping</c> repeat everything <c>ps5</c> already says, which is
    /// the duplication the layer exists to remove.
    /// </remarks>
    [Fact]
    public void Build_WithTwoMatchingTargetKeys_AppliesBothInAscendingSpecificity()
    {
        var policy = Build(
            """
            {
              "schemaVersion": 1,
              "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 10, "patterns": 1 } } },
              "targets": {
                "ps5": { "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 100, "extra": 5 } } } },
                "ps5|Shipping": { "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 200 } } } }
              }
            }
            """,
            Stated("ps5", "Shipping"));

        MaxBytes(policy).ShouldBe(200);

        // The less specific key still contributed the key the more specific one
        // never mentions, and the document's own value survives underneath both.
        policy.ReaderFor(LargeFile.Id).GetValue<long>("extra", 0).ShouldBe(5);
        policy.ReaderFor(LargeFile.Id).GetValue<long>("patterns", 0).ShouldBe(1);
    }

    /// <summary>
    /// The layer sits between the pipeline document and the local overlay.
    /// </summary>
    /// <remarks>
    /// The most important entry here. Above the local overlay, this layer would
    /// take from a developer the ability to loosen a rule on the platform they
    /// are working on — which is the entire use section 6.3 describes.
    /// </remarks>
    [Fact]
    public void Build_WithAMatchingTarget_SitsBetweenThePipelineDocumentAndLocal()
    {
        var local = PolicyDocument.Parse(
            """
            { "schemaVersion": 1, "rules": { "core.presubmit.large-file": { "settings": { "c": 3 } } } }
            """,
            "preflight.local.json");

        var policy = Build(
            """
            {
              "schemaVersion": 1,
              "rules": { "core.presubmit.large-file": { "settings": { "a": 1, "b": 1 } } },
              "targets": {
                "ps5": { "rules": { "core.presubmit.large-file": { "settings": { "b": 2, "c": 2 } } } }
              }
            }
            """,
            Stated("ps5", "Shipping"),
            local);

        var reader = policy.ReaderFor(LargeFile.Id);

        reader.GetValue<long>("a", 0).ShouldBe(1);
        reader.GetValue<long>("b", 0).ShouldBe(2);
        reader.GetValue<long>("c", 0).ShouldBe(3);
    }

    /// <remarks>
    /// ADR-015 says the CLI refuses what it does not understand — and a
    /// <c>--platform</c> no block mentions is not that. It is the common case,
    /// and asserting it explicitly is what stops somebody making it an error
    /// later because it looked like one.
    /// </remarks>
    [Fact]
    public void Build_WithNoMatchingTargetKey_LeavesThePipelineValuesUntouchedAndDoesNotError()
    {
        var policy = Build(
            """
            {
              "schemaVersion": 1,
              "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 10 } } },
              "targets": {
                "ps5": { "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 100 } } } }
              }
            }
            """,
            Stated("win64", "Shipping"));

        MaxBytes(policy).ShouldBe(10);
    }

    /// <remarks>
    /// The layer belongs to the pipeline document and to its <c>extends</c>
    /// ancestry, and nowhere else. Honouring it in the local overlay would turn
    /// a five-layer chain into five layers each with a sub-layer, and the
    /// specificity rule would then have to be defined against the layer rule.
    /// </remarks>
    [Fact]
    public void Build_WithATargetsBlockInTheLocalOverlay_IgnoresIt()
    {
        var local = PolicyDocument.Parse(
            """
            {
              "schemaVersion": 1,
              "targets": {
                "ps5": { "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 999 } } } }
              }
            }
            """,
            "preflight.local.json");

        var policy = Build(
            """
            {
              "schemaVersion": 1,
              "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 10 } } }
            }
            """,
            Stated("ps5", "Shipping"),
            local);

        MaxBytes(policy).ShouldBe(10);
    }

    /// <remarks>
    /// The positive half of the decision above: <c>PolicyLoader</c> merges the
    /// <c>extends</c> chain into one document before this sees it, so an
    /// ancestor's targets arrive as the pipeline's own and must apply.
    /// </remarks>
    [Fact]
    public void Build_WithTargetsInheritedThroughExtends_HonoursThem()
    {
        var policy = Build(
            """
            {
              "schemaVersion": 1,
              "targets": {
                "switch2": { "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 256 } } } }
              }
            }
            """,
            Stated("switch2", "Shipping"));

        MaxBytes(policy).ShouldBe(256);
    }

    /// <remarks>
    /// Empty is a no-op and not an error, in both directions: a block that
    /// matches nothing and a block with nothing in it are both things a policy
    /// author writes on the way to writing the real one.
    /// </remarks>
    [Fact]
    public void Build_WithAnEmptyTargetsBlock_ChangesNothing()
    {
        var policy = Build(
            """
            {
              "schemaVersion": 1,
              "rules": { "core.presubmit.large-file": { "settings": { "maxBytes": 10 } } },
              "targets": {}
            }
            """,
            Stated("ps5", "Shipping"));

        MaxBytes(policy).ShouldBe(10);
    }

    /// <summary>
    /// A value from a target carries where it came from.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>FromRootKey</c>. Without it <c>explain</c> would name the
    /// file and the line and lose the one fact that answers why this run sees
    /// the number and another run does not.
    /// </remarks>
    [Fact]
    public void Build_ForAValueSetByATarget_OriginIsFromTargetWrappingTheFileOrigin()
    {
        var policy = Build(
            """
            {
              "schemaVersion": 1,
              "rules": { "core.presubmit.large-file": { "blocking": true } },
              "targets": {
                "switch2": { "rules": { "core.presubmit.large-file": { "blocking": false } } }
              }
            }
            """,
            Stated("switch2", "Shipping"));

        var value = policy.RuleValue<bool>(LargeFile.Id, "blocking");

        value.Value.ShouldBeFalse();

        var origin = value.Origin.ShouldBeOfType<PolicyOrigin.FromTarget>();

        origin.TargetKey.ShouldBe("switch2");
        origin.Source.ShouldBeOfType<PolicyOrigin.FromFile>().FilePath.ShouldBe("projectc.json");
    }

    /// <summary>
    /// Rules never learn that targets exist.
    /// </summary>
    /// <remarks>
    /// This is how "zero cost per rule" is proved. The layer resolves while the
    /// policy is built, so the reader a rule receives exposes already-resolved
    /// values and carries no target concept — which is also what keeps section
    /// 11.2 from turning this into a major version that recompiles every
    /// plugin.
    /// </remarks>
    [Fact]
    public void IPolicyReader_HasTheSameMembersAsBeforeTheTargetLayer() =>
        typeof(IPolicyReader).GetMembers()
            .Select(member => member.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBe(["GetValue", "TryGetValue"]);
}
