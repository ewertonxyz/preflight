namespace Preflight.Core.Tests.Caching;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Core.Caching;
using Preflight.Core.Plugins;
using Preflight.Core.Policy;

/// <summary>
/// The cache key, one component at a time.
/// </summary>
/// <remarks>
/// The cache key says omitting any of the four components produces a bug, so each
/// of them is varied on its own here — along with the two added later. A key
/// that fails to change is not a failing test in production: it is a
/// <c>Passed</c> served over a workspace that changed, and the evidence of the
/// mistake is the run that did not happen.
/// </remarks>
public sealed class CachePathsTests
{
    private static readonly RuleId Probe = new("core.build.compile-probe");
    private static readonly RuleId Other = new("core.build.configuration");
    private static readonly CacheFingerprint Inputs = new("aaaa");
    private static readonly CacheFingerprint OtherInputs = new("bbbb");
    private static readonly BuildTarget Target = new("win64", "Development");

    /// <summary>A stand-in for the module id of a rule's own assembly.</summary>
    private static readonly Guid RuleAssembly = new("11111111-1111-1111-1111-111111111111");

    private static readonly Guid AnotherRuleAssembly = new("22222222-2222-2222-2222-222222222222");

    private static readonly EngineEnvironment Machine = new()
    {
        ProcessorCount = 8,
        MachineName = "WKS-1234",
        ProcessId = 4242,
    };

    [Fact]
    public void KeyFor_WithIdenticalComponents_IsIdentical() => Key().ShouldBe(Key());

    /// <remarks>
    /// Four rows, one per component of the cache key's table. Each is the bug
    /// that section names: a rule id left out serves one rule's result for
    /// another's; a fingerprint left out never invalidates; a policy digest left
    /// out ignores a changed <c>maxBytes</c>; a generation left out reads a
    /// result serialised under a contract that no longer means the same thing.
    /// </remarks>
    [Fact]
    public void KeyFor_WhenAnyDocumentedComponentChanges_ChangesToo()
    {
        var baseline = Key();

        Key(ruleId: Other).ShouldNotBe(baseline);
        Key(fingerprint: OtherInputs).ShouldNotBe(baseline);
        Key(digest: "other-digest").ShouldNotBe(baseline);
        Key(generation: "0.2").ShouldNotBe(baseline);
    }

    /// <summary>
    /// The two components the cache key's table does not list, and should.
    /// </summary>
    /// <remarks>
    /// A rule reads its stage and its target out of <c>RuleContext</c>, so the
    /// same sources at <c>--platform ps5</c> can legitimately produce a different
    /// answer from the same sources at <c>win64</c>. Leaving them out makes that
    /// a wrong result rather than a lost hit, and it leans on every rule author
    /// remembering to fold them into a fingerprint the engine could fold in for
    /// certain.
    /// </remarks>
    [Fact]
    public void KeyFor_WhenTheStageOrTheTargetChanges_ChangesToo()
    {
        var baseline = Key();

        Key(stage: ValidationStage.PreSubmit).ShouldNotBe(baseline);
        Key(target: new BuildTarget("ps5", "Development")).ShouldNotBe(baseline);
        Key(target: new BuildTarget("win64", "Shipping")).ShouldNotBe(baseline);
    }

    /// <summary>
    /// The last component added: the identity of the rule's own assembly.
    /// </summary>
    /// <remarks>
    /// Without it, a plugin rebuilt with different logic over unchanged inputs
    /// and an unchanged policy is served its predecessor's verdict, and told
    /// <c>(cached)</c> about it. Nothing else in the key notices that a rule's
    /// code changed: the fingerprint describes what the rule read, and the
    /// digest describes how it was configured. Neither describes the rule.
    /// </remarks>
    [Fact]
    public void KeyFor_WhenTheRulesOwnAssemblyChanges_ChangesToo() =>
        Key(assemblyId: AnotherRuleAssembly).ShouldNotBe(Key());

    /// <summary>
    /// Two different sets of components cannot render to the same string.
    /// </summary>
    /// <remarks>
    /// The reason the components are joined with a separator rather than
    /// concatenated. Without one, a rule id ending in a character and a
    /// fingerprint starting with it collide with their neighbours — and a hash
    /// of the wrong thing is indistinguishable from a hash of the right one,
    /// which is the whole difficulty of debugging a cache.
    /// </remarks>
    [Fact]
    public void KeyFor_ForComponentsThatWouldConcatenateAlike_StillDiffers() =>
        Key(ruleId: new RuleId("core.a.ab"), fingerprint: new CacheFingerprint("c"))
            .ShouldNotBe(Key(ruleId: new RuleId("core.a.a"), fingerprint: new CacheFingerprint("bc")));

    [Fact]
    public void AbstractionsGeneration_IsTheGenerationOfTheContractAssembly() =>
        CachePaths.AbstractionsGeneration.ShouldBe(
            AbstractionsCompatibility.GenerationOf(
                typeof(IValidationRule).Assembly.GetName().Version!));

    /// <summary>
    /// Every kind of policy value renders to something of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the test that would have caught a real defect, and did not exist
    /// when the defect was written. <c>PolicyDocument</c> parses every JSON array
    /// in <c>settings</c> into an <c>object?[]</c>, and the first version of the
    /// renderer let its discard arm call <c>ToString()</c> on one — which returns
    /// <c>"System.Object[]"</c> for every array there has ever been. Two
    /// different lists of forbidden path patterns produced the same digest, so
    /// changing the list did not invalidate the cache.
    /// </para>
    /// <para>
    /// Asserted as "all distinct" rather than pair by pair, because the failure
    /// mode is a collision and a pairwise test only finds the pair somebody
    /// thought of.
    /// </para>
    /// </remarks>
    [Fact]
    public void PolicyDigestFor_ForEveryKindOfSettingsValue_ProducesADistinctDigest()
    {
        string[] values =
        [
            "null",
            "true",
            "false",
            "7",
            "1.5",
            "\"x\"",
            "[\"a\"]",
            "[\"b\"]",
            "[\"a\", \"b\"]",
            "[\"b\", \"a\"]",
            "{ \"a\": 1 }",
        ];

        values
            .Select(value => CachePaths.PolicyDigestFor(Probe, PolicyWithRawSetting(value)))
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(values.Length);
    }

    /// <remarks>
    /// Order inside an array is information: two lists of the same patterns in a
    /// different order are different configurations, even when they happen to
    /// behave alike today.
    /// </remarks>
    [Fact]
    public void PolicyDigestFor_WhenAnArraySettingIsReordered_Differs() =>
        CachePaths.PolicyDigestFor(Probe, PolicyWithRawSetting("[\"a\", \"b\"]"))
            .ShouldNotBe(CachePaths.PolicyDigestFor(Probe, PolicyWithRawSetting("[\"b\", \"a\"]")));

    /// <remarks>
    /// The order of keys inside an object is not information, and
    /// <c>RuleEntries</c> already sorts the flattened settings ordinally. Two
    /// policies differing only in how somebody typed them have to reach the same
    /// entry, or the cache misses for a reason nobody could explain.
    /// </remarks>
    [Fact]
    public void PolicyDigestFor_WhenObjectKeysAreReordered_IsIdentical() =>
        CachePaths.PolicyDigestFor(Probe, PolicyWithRawSetting("{ \"x\": 1, \"y\": 2 }"))
            .ShouldBe(CachePaths.PolicyDigestFor(Probe, PolicyWithRawSetting("{ \"y\": 2, \"x\": 1 }")));

    /// <remarks>
    /// The five keys the policy schema declares per rule, each varied on its own.
    /// <c>enabled</c> is in the list even though a disabled rule never reaches
    /// the runner: the digest is a statement about the whole effective policy,
    /// and leaving a key out because today's caller cannot reach it is how a
    /// component quietly stops being part of the key.
    /// </remarks>
    [Theory]
    [InlineData("\"enabled\": true", "\"enabled\": false")]
    [InlineData("\"blocking\": true", "\"blocking\": false")]
    [InlineData("\"gating\": true", "\"gating\": false")]
    [InlineData("\"severity\": \"warning\"", "\"severity\": \"error\"")]
    [InlineData("\"timeoutSeconds\": 30", "\"timeoutSeconds\": 60")]
    public void PolicyDigestFor_ForEachDeclaredRuleKey_ChangesWithIt(string one, string another) =>
        CachePaths.PolicyDigestFor(Probe, PolicyWithRuleEntry(one))
            .ShouldNotBe(CachePaths.PolicyDigestFor(Probe, PolicyWithRuleEntry(another)));

    /// <summary>
    /// The digest tracks the effective value and ignores where it came from.
    /// </summary>
    /// <remarks>
    /// The same limit reached through a differently named overlay is the same
    /// configuration. Hashing the provenance would throw away every hit on a
    /// machine that merely calls its policy file something else — a cache that
    /// never warms, for no correctness gain at all.
    /// </remarks>
    [Fact]
    public void PolicyDigestFor_IgnoresProvenanceAndTracksTheValue()
    {
        var fromOne = PolicyWith("preflight.base.json", 4096);
        var fromAnother = PolicyWith("preflight.atlas.json", 4096);
        var different = PolicyWith("preflight.base.json", 8192);

        CachePaths.PolicyDigestFor(Probe, fromAnother)
            .ShouldBe(CachePaths.PolicyDigestFor(Probe, fromOne));

        CachePaths.PolicyDigestFor(Probe, different)
            .ShouldNotBe(CachePaths.PolicyDigestFor(Probe, fromOne));
    }

    [Fact]
    public void PolicyDigestFor_ForTwoRulesUnderOnePolicy_Differs() =>
        CachePaths.PolicyDigestFor(Probe, PolicyWith("preflight.base.json", 4096))
            .ShouldNotBe(CachePaths.PolicyDigestFor(Other, PolicyWith("preflight.base.json", 4096)));

    [Fact]
    public void DirectoryFor_ForARelativePath_ResolvesAgainstTheWorkspaceRoot()
    {
        var workspace = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "preflight-cache-paths"));

        CachePaths.DirectoryFor(workspace, ".preflight/cache")
            .ShouldBe(Path.Combine(workspace.FullName, ".preflight/cache"));
    }

    [Fact]
    public void DirectoryFor_ForARootedPath_LeavesItAlone()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "preflight-shared-cache");

        CachePaths.DirectoryFor(new DirectoryInfo(Path.GetTempPath()), rooted).ShouldBe(rooted);
    }

    /// <remarks>
    /// A directory per rule, as the cache key draws it. It is what makes the
    /// layout readable by hand when somebody is trying to work out why a result
    /// came back cached.
    /// </remarks>
    [Fact]
    public void FileFor_IsTheLayoutSection123Draws() =>
        CachePaths.FileFor("/w/.preflight/cache", Probe, "abc")
            .ShouldBe(Path.Combine("/w/.preflight/cache", "core.build.compile-probe", "abc.json"));

    private static string Key(
        RuleId? ruleId = null,
        CacheFingerprint? fingerprint = null,
        string digest = "digest",
        ValidationStage stage = ValidationStage.BuildReadiness,
        BuildTarget? target = null,
        string generation = "0.1",
        Guid? assemblyId = null) =>
        CachePaths.KeyFor(
            ruleId ?? Probe,
            fingerprint ?? Inputs,
            digest,
            stage,
            target ?? Target,
            generation,
            assemblyId ?? RuleAssembly);

    private static EffectivePolicy PolicyWithRawSetting(string raw) => Build(
        "{ \"schemaVersion\": 1, \"rules\": { \"" + Probe.Value +
        "\": { \"settings\": { \"value\": " + raw + " } } } }");

    private static EffectivePolicy PolicyWithRuleEntry(string entry) => Build(
        "{ \"schemaVersion\": 1, \"rules\": { \"" + Probe.Value + "\": { " + entry + " } } }");

    private static EffectivePolicy PolicyWith(string file, int timeoutSeconds) => EffectivePolicy.Build(
        [Descriptor(Probe), Descriptor(Other)],
        PolicyDocument.Parse(
            "{ \"schemaVersion\": 1, \"rules\": { \"" + Probe.Value + "\": { \"timeoutSeconds\": " +
            timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " } } }",
            file),
        local: null,
        setOverrides: [],
        target: StatedBuildTarget.Unstated,
        Machine);

    private static EffectivePolicy Build(string json) => EffectivePolicy.Build(
        [Descriptor(Probe), Descriptor(Other)],
        PolicyDocument.Parse(json, "preflight.base.json"),
        local: null,
        setOverrides: [],
        target: StatedBuildTarget.Unstated,
        Machine);

    private static RuleDescriptor Descriptor(RuleId id) => new()
    {
        Id = id,
        DisplayName = id.Value,
        Stage = ValidationStage.BuildReadiness,
    };
}
