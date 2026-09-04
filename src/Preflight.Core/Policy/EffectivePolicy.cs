namespace Preflight.Core.Policy;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;

/// <summary>
/// The fully resolved policy: <c>RuleDescriptor</c> defaults merged under the
/// production document, the local document, and any <c>--set</c> overrides, in
/// that order.
/// </summary>
/// <remarks>
/// The whole precedence chain, built from one call to
/// <see cref="PolicyNode.Merge"/> per layer — the same primitive
/// <see cref="PolicyLoader"/> uses for <c>extends</c>. A layer that is
/// <see langword="null"/> (no <c>local</c> file, for instance) is simply
/// skipped; deciding whether <c>local</c> applies at all — CI detection,
/// <c>--no-local</c>, <c>--allow-local</c> — is the CLI's job in
/// <c>Preflight.Cli</c>.
/// </remarks>
public sealed class EffectivePolicy
{
    private readonly PolicyNode _root;

    private EffectivePolicy(PolicyNode root)
    {
        _root = root;
    }

    /// <param name="environment">
    /// The machine facts the engine defaults are derived from. Defaults to the
    /// real machine; a test passes a fixed one so that
    /// <c>maxDegreeOfParallelism</c> stops being a core count in a golden file.
    /// See <see cref="EngineEnvironment"/>.
    /// </param>
    /// <param name="target">
    /// What the run is aimed at, and which halves of it the user actually said.
    /// Required rather than defaulted: a target that fell back to a value would
    /// switch this layer off in silence, and silence is what the layer exists
    /// to remove. See ADR-030.
    /// </param>
    public static EffectivePolicy Build(
        IReadOnlyList<RuleDescriptor> descriptors,
        PolicyDocument? pipeline,
        PolicyDocument? local,
        IReadOnlyList<PolicySetOverride> setOverrides,
        StatedBuildTarget target,
        EngineEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        PolicyNode node = BuildDefaults(descriptors, environment ?? EngineEnvironment.Current);

        if (pipeline is not null)
        {
            node = PolicyNode.Merge(node, pipeline.Root);

            // After the document it belongs to and before the local overlay.
            // Above local would take from a developer the ability to loosen a
            // rule on the platform they are working on, which is the whole use
            // of section 6.3.
            node = ApplyTargets(node, pipeline, target);
        }

        if (local is not null)
        {
            node = PolicyNode.Merge(node, local.Root);
        }

        foreach (var setOverride in setOverrides)
        {
            node = PolicyNode.Merge(node, setOverride.ToNode());
        }

        return new EffectivePolicy(CascadeRootTimeout(node));
    }

    /// <summary>
    /// Merges every <c>targets</c> entry that applies, least specific first.
    /// </summary>
    /// <remarks>
    /// Once, here, and never per rule. The layer resolves while the policy is
    /// being built, so <c>IPolicyReader</c> is unchanged and no rule — built in
    /// or plugin — knows a target exists. That is what keeps the cost off the
    /// hot path and what stops section 11.2 from turning this into a major
    /// version. See ADR-030.
    /// </remarks>
    private static PolicyNode ApplyTargets(PolicyNode node, PolicyDocument pipeline, StatedBuildTarget target)
    {
        if (pipeline.Root is not PolicyNode.ObjectNode root ||
            root.Members.GetValueOrDefault("targets") is not PolicyNode.ObjectNode targets)
        {
            return node;
        }

        // Ascending specificity, then ordinal by key. Every matching block
        // applies rather than only the most specific one: the alternative makes
        // 'ps5|Shipping' repeat everything 'ps5' already says, which is the
        // duplication this layer exists to remove. The second sort key is what
        // keeps two runs of the same policy byte-identical when a dictionary
        // hands the members back in a different order.
        var applicable = targets.Members
            .Select(member => (Text: member.Key, Node: member.Value, Parsed: Parse(member.Key)))
            .Where(entry => entry.Parsed is { } key && key.Matches(target))
            .OrderBy(entry => entry.Parsed!.Value.Specificity)
            .ThenBy(entry => entry.Text, StringComparer.Ordinal);

        foreach (var entry in applicable)
        {
            node = PolicyNode.Merge(node, Attribute(entry.Node, entry.Text));
        }

        return node;

        static PolicyTargetKey? Parse(string text) =>
            PolicyTargetKey.TryParse(text, out var key) ? key : null;
    }

    /// <summary>
    /// Rewrites a target block's origins so every value it carries says which
    /// target key put it there.
    /// </summary>
    /// <remarks>
    /// Without this the values would arrive wearing the plain file origin, and
    /// <c>explain</c> would name the file and the line while losing the one
    /// fact that answers why this run sees the number and another run does not.
    /// </remarks>
    private static PolicyNode Attribute(PolicyNode node, string targetKey)
    {
        // Guarded returns rather than a switch expression, for the reason
        // PolicyNode.Merge uses them: the hierarchy has exactly two shapes, and
        // a switch would need a discard arm nothing can reach — a permanent
        // hole in the branch count, guarding a case that does not exist.
        if (node is PolicyNode.ObjectNode branch)
        {
            return new PolicyNode.ObjectNode(branch.Members.ToDictionary(
                member => member.Key,
                member => Attribute(member.Value, targetKey),
                StringComparer.Ordinal));
        }

        // A cast, not a test. PolicyNode has a private constructor and exactly
        // two nested shapes, and the object one returned above — so a test here
        // would guard a case the type system already forbids and leave a branch
        // no input can reach.
        var leaf = (PolicyNode.Leaf)node;

        return new PolicyNode.Leaf(new PolicyValue<object?>
        {
            Entries =
            [
                .. leaf.Value.Entries.Select(entry =>
                    entry with { Origin = new PolicyOrigin.FromTarget(targetKey, entry.Origin) }),
            ],
        });
    }

    /// <summary>
    /// Applies the root <c>defaultTimeoutSeconds</c> to every rule that did not
    /// state a <c>timeoutSeconds</c> of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rule's <c>timeoutSeconds</c> defaults to the root
    /// <c>defaultTimeoutSeconds</c>, which is a second axis of precedence
    /// sitting across the layer order of the merge — and the two axes resolve
    /// in different directions, so this cannot fall out of the merge on its
    /// own.
    /// </para>
    /// <para>
    /// Specificity wins over layer order here, and the key's own name is the
    /// argument: <c>defaultTimeoutSeconds</c> is a <em>default</em>, so it
    /// fills a gap rather than overrides a statement. A rule that names its own
    /// <c>timeoutSeconds</c> keeps it even when a later layer sets the root
    /// default — otherwise adding one line to a local overlay would silently
    /// retune every rule that had been deliberately given its own budget.
    /// </para>
    /// <para>
    /// The root default still outranks
    /// <see cref="RuleDescriptor.DefaultTimeoutSeconds"/>, because every
    /// <c>Default</c>-prefixed descriptor field is only a default, and policy
    /// has the final word. So the ordering within this one value is: descriptor
    /// default, then root <c>defaultTimeoutSeconds</c> if any layer set it,
    /// then the rule's own <c>timeoutSeconds</c> if any layer set that.
    /// </para>
    /// </remarks>
    private static PolicyNode CascadeRootTimeout(PolicyNode merged)
    {
        if (merged is not PolicyNode.ObjectNode root ||
            root.Members.GetValueOrDefault("defaultTimeoutSeconds") is not PolicyNode.Leaf rootDefault ||
            rootDefault.Value.Origin is PolicyOrigin.EngineDefault ||
            root.Members.GetValueOrDefault("rules") is not PolicyNode.ObjectNode rules)
        {
            return merged;
        }

        var cascadedOrigin = new PolicyOrigin.FromRootKey("defaultTimeoutSeconds", rootDefault.Value.Origin);
        var rewrittenRules = new Dictionary<string, PolicyNode>(rules.Members);
        var changed = false;

        foreach (var (ruleId, ruleNode) in rules.Members)
        {
            if (ruleNode is not PolicyNode.ObjectNode rule ||
                rule.Members.GetValueOrDefault("timeoutSeconds") is not PolicyNode.Leaf timeout ||
                timeout.Value.Origin is not PolicyOrigin.DescriptorDefault)
            {
                continue;
            }

            rewrittenRules[ruleId] = new PolicyNode.ObjectNode(new Dictionary<string, PolicyNode>(rule.Members)
            {
                ["timeoutSeconds"] = new PolicyNode.Leaf(
                    timeout.Value.OverriddenBy(rootDefault.Value.Value, cascadedOrigin)),
            });

            changed = true;
        }

        return changed
            ? new PolicyNode.ObjectNode(new Dictionary<string, PolicyNode>(root.Members)
            {
                ["rules"] = new PolicyNode.ObjectNode(rewrittenRules),
            })
            : merged;
    }

    public PolicyValue<T> RootValue<T>(string key) => Convert<T>(RequireLeaf(key.Split('.')));

    public PolicyValue<T> RuleValue<T>(RuleId ruleId, string key) =>
        Convert<T>(RequireLeaf(RulePath(ruleId, key)));

    public IPolicyReader ReaderFor(RuleId ruleId)
    {
        _root.TryGetPath(RulePath(ruleId, "settings"), out var settingsNode);

        return new ScopedPolicyReader(settingsNode as PolicyNode.ObjectNode ?? EmptyObject());
    }

    /// <summary>
    /// Every effective value for one rule, in the order
    /// <c>preflight explain</c> prints them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>explain</c> prints a row per effective key, including
    /// <c>settings.maxBytes</c> — and the command cannot know that key's name
    /// in advance, because the schema leaves <c>settings</c> uninspected on
    /// purpose. The other three members of this class all answer "what is the
    /// value at this path", which is the wrong question here.
    /// </para>
    /// <para>
    /// The alternative — letting <c>explain</c> walk the merged tree itself —
    /// puts a second traversal of the same structure in a project that does not
    /// own it, and two traversals of one concept diverge in silence. That is
    /// the same argument that fixes the order of the report, applied to
    /// enumeration.
    /// </para>
    /// <para>
    /// Order is fixed, because a report has to be diffable between runs and
    /// this list is a report: the declared rule keys first, in the order
    /// <see cref="PolicyKeySchema.RuleKeyOrder"/> declares them, then every
    /// <c>settings</c> leaf sorted ordinally by its full dotted key. Nested
    /// settings are flattened, so a <c>settings.limits.maxBytes</c> arrives as
    /// one row and not as an object nobody can print.
    /// </para>
    /// <para>
    /// Returns an empty list for a rule the policy has never heard of, rather
    /// than throwing. An unknown id is the caller's question to answer — with a
    /// suggestion — and it can only answer it if it gets a value back.
    /// </para>
    /// </remarks>
    public IReadOnlyList<EffectivePolicyEntry> RuleEntries(RuleId ruleId)
    {
        if (!_root.TryGetPath(["rules", ruleId.Value], out var node) || node is not PolicyNode.ObjectNode rule)
        {
            return [];
        }

        var entries = new List<EffectivePolicyEntry>();

        foreach (var definition in PolicyKeySchema.RuleKeyOrder)
        {
            if (!rule.Members.TryGetValue(definition.Name, out var member))
            {
                continue;
            }

            if (member is PolicyNode.Leaf leaf)
            {
                entries.Add(new EffectivePolicyEntry(definition.Name, leaf.Value));
                continue;
            }

            var nested = new List<EffectivePolicyEntry>();
            Flatten(definition.Name, member, nested);
            nested.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));
            entries.AddRange(nested);
        }

        return entries;
    }

    /// <summary>
    /// Walks an object subtree into one entry per leaf, keyed by its dotted
    /// path.
    /// </summary>
    /// <remarks>
    /// Recursive rather than iterative, unlike <c>RuleGraph</c>'s traversal.
    /// The depth here is bounded by how deeply a human nested a JSON object
    /// inside <c>settings</c>, not by a rule count that grows with the project,
    /// and <c>PolicyDocument</c> already parses that same structure recursively
    /// — an iterative walk here would guard a depth the parser has already
    /// survived.
    /// </remarks>
    private static void Flatten(string prefix, PolicyNode node, List<EffectivePolicyEntry> into)
    {
        if (node is PolicyNode.Leaf leaf)
        {
            into.Add(new EffectivePolicyEntry(prefix, leaf.Value));

            return;
        }

        // Cast, not a second pattern match. PolicyNode is a closed hierarchy —
        // abstract, private constructor, two nested sealed records — so a node
        // that is not a Leaf is an ObjectNode. A `switch` over both cases, or an
        // `else if`, would compile a third path that no input can reach, and an
        // unreachable branch is either a permanent hole in the branch count or a
        // fabricated test written to close it. If a third node type is ever
        // added, this throws at the first settings tree that contains one, which
        // is the loud failure the project prefers over a silently dropped row.
        foreach (var (key, child) in ((PolicyNode.ObjectNode)node).Members)
        {
            Flatten($"{prefix}.{key}", child, into);
        }
    }

    /// <summary>
    /// Builds a path that safely crosses a rule id: the id is one segment,
    /// never re-split even though it contains dots of its own. Only
    /// <paramref name="key"/> is split.
    /// </summary>
    private static string[] RulePath(RuleId ruleId, string key) => ["rules", ruleId.Value, .. key.Split('.')];

    private static PolicyNode.ObjectNode BuildDefaults(
        IReadOnlyList<RuleDescriptor> descriptors,
        EngineEnvironment environment)
    {
        var rootMembers = new Dictionary<string, PolicyNode>
        {
            ["maxDegreeOfParallelism"] = Leaf((long)environment.ProcessorCount, new PolicyOrigin.EngineDefault()),
            ["defaultTimeoutSeconds"] = Leaf(60L, new PolicyOrigin.EngineDefault()),
            ["historyPath"] = Leaf(".preflight/history", new PolicyOrigin.EngineDefault()),
            ["historyMode"] = Leaf("shared", new PolicyOrigin.EngineDefault()),
            ["cachePath"] = Leaf(".preflight/cache", new PolicyOrigin.EngineDefault()),
        };

        var rules = new Dictionary<string, PolicyNode>();

        foreach (var descriptor in descriptors)
        {
            rules[descriptor.Id.Value] = new PolicyNode.ObjectNode(new Dictionary<string, PolicyNode>
            {
                ["enabled"] = Leaf(true, new PolicyOrigin.EngineDefault()),
                ["blocking"] = Leaf(descriptor.DefaultBlocking, new PolicyOrigin.DescriptorDefault()),
                ["gating"] = Leaf(descriptor.DefaultGating, new PolicyOrigin.DescriptorDefault()),
                ["severity"] = Leaf(SeverityToRaw(descriptor.DefaultSeverity), new PolicyOrigin.DescriptorDefault()),
                ["timeoutSeconds"] = Leaf((long)descriptor.DefaultTimeoutSeconds, new PolicyOrigin.DescriptorDefault()),
                ["settings"] = EmptyObject(),
            });
        }

        rootMembers["rules"] = new PolicyNode.ObjectNode(rules);

        return new PolicyNode.ObjectNode(rootMembers);
    }

    private static PolicyNode.ObjectNode EmptyObject() => new(new Dictionary<string, PolicyNode>());

    private static PolicyNode.Leaf Leaf(object? value, PolicyOrigin origin) =>
        new(PolicyValue.Initial(value, origin));

    private static string SeverityToRaw(Severity severity) => severity switch
    {
        Severity.Information => "information",
        Severity.Warning => "warning",
        Severity.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    private PolicyValue<object?> RequireLeaf(IReadOnlyList<string> path)
    {
        if (!_root.TryGetPath(path, out var node) || node is not PolicyNode.Leaf leaf)
        {
            throw new InvalidOperationException($"No effective value at '{string.Join('.', path)}'.");
        }

        return leaf.Value;
    }

    private static PolicyValue<T> Convert<T>(PolicyValue<object?> raw) => new()
    {
        Entries = [.. raw.Entries.Select(entry =>
            new PolicyValueEntry<T>(PolicyValueConversion.Convert<T>(entry.Value), entry.Origin))],
    };
}
