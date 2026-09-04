namespace Preflight.Core.Policy;

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Preflight.Abstractions.Rules;

/// <summary>
/// Validates already-parsed policy input against the schema.
/// </summary>
/// <remarks>
/// <para>
/// Never throws itself, and never stops at the first problem: the user chose to
/// accumulate every error found across every document in the load and report
/// them together. It is the caller's job to decide that a non-empty result is
/// fatal and raise <see cref="PolicyValidationException"/>.
/// </para>
/// <para>
/// Every scope — root keys, per-rule keys, and a single <c>--set</c> override —
/// is checked by the same walk, parameterised by
/// <see cref="PolicyKeySchema"/>. There is deliberately no second,
/// hand-written validator per scope: see the remarks on that type for why one
/// table beats several.
/// </para>
/// </remarks>
public static class PolicyValidator
{
    private const int SupportedSchemaVersion = 1;

    /// <summary>
    /// Where a value being validated came from: how to name it in a message,
    /// and the file to record on the error when there is one.
    /// </summary>
    /// <remarks>
    /// A <c>--set</c> override has no file and no line, but still needs to be
    /// named in the error text — hence the two fields rather than one.
    /// </remarks>
    private readonly record struct ValidationSource(string Description, string? FilePath)
    {
        public static ValidationSource ForFile(string filePath) => new($"'{filePath}'", filePath);

        public static ValidationSource ForSetOverride() => new("the '--set' override", null);
    }

    public static IReadOnlyList<PolicyValidationError> ValidateAll(
        IEnumerable<PolicyDocument> documents,
        IReadOnlyList<RuleDescriptor> descriptors)
    {
        var errors = new List<PolicyValidationError>();
        var knownRuleIds = KnownRuleIds(descriptors);

        foreach (var document in documents)
        {
            ValidateDocument(document, knownRuleIds, errors);
        }

        return errors;
    }

    /// <summary>
    /// Validates one already-typed <c>--set</c> override against the same key
    /// table a policy file is held to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>--set</c> sits at the top of the precedence chain, which makes it the
    /// one layer able to override every other — so leaving it unchecked meant
    /// the strictest input was the least validated. <c>--set :rules=oops</c>
    /// used to sail past
    /// <see cref="PolicyKeySchema"/> entirely and be absorbed by a silent
    /// shape-mismatch fallback deep inside the merge, which is exactly the
    /// late, quiet failure that validating at load exists to prevent.
    /// </para>
    /// <para>
    /// This cannot be expressed by wrapping the override in a
    /// <see cref="PolicyDocument"/> and calling <see cref="ValidateAll"/>: that
    /// path is gated on <c>schemaVersion</c>, and a single command-line flag
    /// has none to offer, so every override would fail with a spurious
    /// "schemaVersion is missing".
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PolicyValidationError> ValidateSetOverride(
        PolicySetOverride setOverride,
        IReadOnlyList<RuleDescriptor> descriptors)
    {
        var errors = new List<PolicyValidationError>();
        var source = ValidationSource.ForSetOverride();

        // ToNode always builds object nodes down to the leaf, so both casts
        // below are shape-guaranteed by construction.
        var root = (PolicyNode.ObjectNode)setOverride.ToNode();

        if (setOverride.RuleId is not { } ruleId)
        {
            ValidateScope(root, PolicyKeySchema.RootKeys, source, jsonPathPrefix: string.Empty, errors);
            return errors;
        }

        var knownRuleIds = KnownRuleIds(descriptors);

        if (!knownRuleIds.Contains(ruleId.Value, StringComparer.Ordinal))
        {
            errors.Add(new PolicyValidationError(
                Suggested($"Unknown rule id '{ruleId.Value}' in {source.Description}.", ruleId.Value, knownRuleIds),
                source.FilePath,
                null,
                $"rules.{ruleId.Value}"));

            return errors;
        }

        root.TryGetPath(["rules", ruleId.Value], out var ruleNode);

        ValidateScope(
            (PolicyNode.ObjectNode)ruleNode!, PolicyKeySchema.RuleKeys, source, $"rules.{ruleId.Value}.", errors);

        return errors;
    }

    private static string[] KnownRuleIds(IReadOnlyList<RuleDescriptor> descriptors) =>
        [.. descriptors.Select(descriptor => descriptor.Id.Value)];

    private static void ValidateDocument(PolicyDocument document, string[] knownRuleIds, List<PolicyValidationError> errors)
    {
        if (!TryValidateSchemaVersion(document, errors))
        {
            // Deliberately abandons the rest of this document:
            // reading what is recognised out of a file written for a schema
            // this binary does not know would run a weaker set of checks than
            // the author asked for, and then report success.
            return;
        }

        // Reaching here means TryValidateSchemaVersion found a schemaVersion,
        // and TryGetPath can only find one inside an object — so the root is
        // an ObjectNode by that method's postcondition, not by hope.
        var rootObject = (PolicyNode.ObjectNode)document.Root;
        var source = ValidationSource.ForFile(document.FilePath);

        ValidateScope(rootObject, PolicyKeySchema.RootKeys, source, jsonPathPrefix: string.Empty, errors);
        RefuseBothPipelineSpellings(rootObject, document, errors);

        if (rootObject.Members.GetValueOrDefault("targets") is PolicyNode.ObjectNode targets)
        {
            ValidateTargetMap(targets, knownRuleIds, source, errors);
        }

        if (rootObject.Members.GetValueOrDefault("rules") is PolicyNode.ObjectNode rules)
        {
            ValidateRuleMap(rules, knownRuleIds, source, errors);
        }
    }

    /// <summary>
    /// Refuses a document that names the pipeline twice, once in each spelling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>production</c> stays in the schema because removing it would turn
    /// every policy file written before ADR-027 into a load-time error under a
    /// table that is strict by design. Accepting both at once is a different
    /// question, and the answer is the one the CLI gives for <c>--pipeline</c>
    /// with <c>--production</c>: two spellings of one key define no precedence
    /// between them, so honouring either would decide for the author which of
    /// two names they meant.
    /// </para>
    /// <para>
    /// Refused even when the two carry the same value. Comparing values would
    /// make the rule depend on what was written rather than on the fact that
    /// two names were used, and an author halfway through a migration would be
    /// told nothing on the file that still says both.
    /// </para>
    /// </remarks>
    private static void RefuseBothPipelineSpellings(
        PolicyNode.ObjectNode root, PolicyDocument document, List<PolicyValidationError> errors)
    {
        if (!root.Members.TryGetValue("production", out var deprecated) ||
            !root.Members.ContainsKey("pipeline"))
        {
            return;
        }

        errors.Add(new PolicyValidationError(
            $"'pipeline' and 'production' are both set in '{document.FilePath}'. " +
            "'production' is the deprecated spelling of 'pipeline'; keep one.",
            document.FilePath,
            LineOf(deprecated),
            "production"));
    }

    private static bool TryValidateSchemaVersion(PolicyDocument document, List<PolicyValidationError> errors)
    {
        if (!document.Root.TryGetPath("schemaVersion", out var node) ||
            node is not PolicyNode.Leaf { Value.Value: long schemaVersion })
        {
            errors.Add(new PolicyValidationError(
                $"'schemaVersion' is missing in '{document.FilePath}'.", document.FilePath, null, "schemaVersion"));
            return false;
        }

        if (schemaVersion != SupportedSchemaVersion)
        {
            errors.Add(new PolicyValidationError(
                $"Unknown schemaVersion {schemaVersion} in '{document.FilePath}'. " +
                $"This binary only understands schemaVersion {SupportedSchemaVersion} and will not read a newer file best-effort.",
                document.FilePath,
                LineOf(node),
                "schemaVersion"));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reports every layer that overrides a path an earlier layer sealed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="ValidateAll"/> because it needs what that
    /// method is not given: the chain in order, the local overlay, and the
    /// command-line overrides. A seal is about the relationship between layers,
    /// and no single document can be checked for it alone.
    /// </para>
    /// <para>
    /// Presence is not the test — value is. A file that writes the value the
    /// seal already fixed agrees with the policy, and refusing it would tell
    /// the author that agreeing was forbidden. See ADR-031.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PolicyValidationError> ValidateSeals(
        PolicySeal seal,
        IReadOnlyList<PolicyDocument> chain,
        PolicyDocument? local,
        IReadOnlyList<PolicySetOverride> overrides,
        IReadOnlyList<RuleDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(seal);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(overrides);

        var errors = new List<PolicyValidationError>();

        if (seal.IsEmpty)
        {
            return errors;
        }

        // Each chain document is bound only by the seals declared before it, so
        // a baseline can state the value it protects. Everything after the
        // chain — a targets block, the overlay, the command line — is bound by
        // all of them.
        foreach (var document in chain)
        {
            CheckDocument(
                seal, chain, document, document.FilePath, ValidationSource.ForFile(document.FilePath), errors);
            CheckTargets(seal, chain, document, errors);
        }

        if (local is not null)
        {
            CheckDocument(seal, chain, local, null, ValidationSource.ForFile(local.FilePath), errors);
        }

        foreach (var setOverride in overrides)
        {
            var ruleId = setOverride.RuleId?.Value;

            if (seal.IsSealed(ruleId, setOverride.Path, out var declaredBy) &&
                !Agrees(chain, declaredBy, ruleId, setOverride.Path, setOverride.TypedValue))
            {
                errors.Add(Violation(declaredBy, ValidationSource.ForSetOverride(), null, setOverride.Path));
            }
        }

        return errors;
    }

    /// <summary>
    /// Walks one document's rules and root keys against the seals that bind it.
    /// </summary>
    private static void CheckDocument(
        PolicySeal seal,
        IReadOnlyList<PolicyDocument> chain,
        PolicyDocument document,
        string? afterFilePath,
        ValidationSource source,
        List<PolicyValidationError> errors,
        string jsonPathPrefix = "")
    {
        if (document.Root is not PolicyNode.ObjectNode root)
        {
            return;
        }

        CheckScope(seal, chain, root, afterFilePath, source, errors, jsonPathPrefix);
    }

    private static void CheckScope(
        PolicySeal seal,
        IReadOnlyList<PolicyDocument> chain,
        PolicyNode.ObjectNode root,
        string? afterFilePath,
        ValidationSource source,
        List<PolicyValidationError> errors,
        string jsonPathPrefix)
    {
        foreach (var (key, node) in root.Members)
        {
            if (key is "rules" or "targets" or PolicySeal.KeyName)
            {
                continue;
            }

            CheckLeaves(seal, chain, node, ruleId: null, key, afterFilePath, source, errors, jsonPathPrefix + key);
        }

        if (root.Members.GetValueOrDefault("rules") is not PolicyNode.ObjectNode rules)
        {
            return;
        }

        foreach (var (ruleId, ruleNode) in rules.Members)
        {
            if (ruleNode is not PolicyNode.ObjectNode rule)
            {
                continue;
            }

            foreach (var (key, node) in rule.Members)
            {
                CheckLeaves(
                    seal, chain, node, ruleId, key, afterFilePath, source, errors,
                    $"{jsonPathPrefix}rules.{ruleId}.{key}");
            }
        }
    }

    /// <summary>
    /// Descends to the leaves, because a seal names a value and not a subtree.
    /// </summary>
    /// <remarks>
    /// <c>settings</c> is the reason: sealing <c>settings.maxBytes</c> must
    /// refuse a file that writes that one key and leave every other setting of
    /// the same rule alone.
    /// </remarks>
    private static void CheckLeaves(
        PolicySeal seal,
        IReadOnlyList<PolicyDocument> chain,
        PolicyNode node,
        string? ruleId,
        string keyPath,
        string? afterFilePath,
        ValidationSource source,
        List<PolicyValidationError> errors,
        string jsonPath)
    {
        if (node is PolicyNode.ObjectNode branch)
        {
            foreach (var (key, child) in branch.Members)
            {
                CheckLeaves(
                    seal, chain, child, ruleId, $"{keyPath}.{key}", afterFilePath, source, errors,
                    $"{jsonPath}.{key}");
            }

            return;
        }

        if (!seal.IsSealed(ruleId, keyPath, out var declaredBy, afterFilePath))
        {
            return;
        }

        if (node is PolicyNode.Leaf leaf && Agrees(chain, declaredBy, ruleId, keyPath, leaf.Value.Value))
        {
            return;
        }

        errors.Add(Violation(declaredBy, source, LineOf(node), jsonPath));
    }

    /// <remarks>
    /// A target block belongs to the document that declared it but is applied
    /// after the whole chain, so every seal binds it — including one the same
    /// file declared. A file that seals a path and then moves it in its own
    /// target block is contradicting itself, and the cheapest answer is to say
    /// so rather than to pick a winner.
    /// </remarks>
    private static void CheckTargets(
        PolicySeal seal,
        IReadOnlyList<PolicyDocument> chain,
        PolicyDocument document,
        List<PolicyValidationError> errors)
    {
        if (document.Root is not PolicyNode.ObjectNode root ||
            root.Members.GetValueOrDefault("targets") is not PolicyNode.ObjectNode targets)
        {
            return;
        }

        var source = ValidationSource.ForFile(document.FilePath);

        foreach (var (targetKey, block) in targets.Members)
        {
            if (block is PolicyNode.ObjectNode scope)
            {
                CheckScope(seal, chain, scope, afterFilePath: null, source, errors, $"targets.{targetKey}.");
            }
        }
    }

    /// <summary>
    /// Whether a downstream layer writes the value the sealing file already
    /// fixed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Presence is not the offence — change is. Refusing a file that repeats
    /// the sealed value would turn agreement with the policy into an error, and
    /// the author would be told that agreeing was forbidden.
    /// </para>
    /// <para>
    /// Comparing values is safe because the schema types everything the parser
    /// produces: a boolean is a <see langword="bool"/> and an integer is a
    /// <see langword="long"/>, checked before this runs. Inside <c>settings</c>
    /// nothing is typed, so <c>1</c> and <c>"1"</c> compare unequal — which is
    /// the right answer, because they are different values.
    /// </para>
    /// <para>
    /// A file that seals a path without stating a value fixes nothing, so every
    /// downstream write of it is a change.
    /// </para>
    /// </remarks>
    private static bool Agrees(
        IReadOnlyList<PolicyDocument> chain,
        SealSource declaredBy,
        string? ruleId,
        string keyPath,
        object? candidate)
    {
        // First, and deliberately not a TryGetValue with a fallback: the seal
        // was read out of this very chain, so the file is in it. A guard here
        // would be a branch no input can take, standing in for an invariant
        // the caller already holds.
        var declaring = chain.First(document =>
            string.Equals(document.FilePath, declaredBy.FilePath, StringComparison.OrdinalIgnoreCase));

        // The segment overload, never the dotted one: a rule id contains dots,
        // so a path built by joining would split back in the wrong places —
        // the same ambiguity that gave --set its ':' separator.
        List<string> segments = ruleId is null ? [] : ["rules", ruleId];

        segments.AddRange(keyPath.Split('.'));

        return declaring.Root.TryGetPath(segments, out var node) &&
            node is PolicyNode.Leaf sealedLeaf &&
            Equals(sealedLeaf.Value.Value, candidate);
    }

    private static PolicyValidationError Violation(
        SealSource declaredBy, ValidationSource source, int? line, string jsonPath) =>
        new(
            $"'{jsonPath}' is sealed by '{declaredBy.Pattern}' in '{declaredBy.FilePath}' " +
            $"and cannot be overridden in {source.Description}.",
            source.FilePath,
            line,
            jsonPath);

    /// <summary>
    /// Walks a <c>targets</c> block: every key must parse, no two keys may be
    /// the same key in two spellings, and the inside of each is a root scope.
    /// </summary>
    /// <remarks>
    /// A key nobody can parse is a block that silently never applies, which is
    /// principle 7 written into a policy file — and the case collision is
    /// worse, because both keys match at the same specificity and the winner
    /// would come from dictionary order rather than from anything anybody
    /// wrote. See ADR-030.
    /// </remarks>
    private static void ValidateTargetMap(
        PolicyNode.ObjectNode targets,
        string[] knownRuleIds,
        ValidationSource source,
        List<PolicyValidationError> errors)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, node) in targets.Members)
        {
            var jsonPath = $"targets.{key}";

            if (!PolicyTargetKey.TryParse(key, out _))
            {
                errors.Add(new PolicyValidationError(
                    $"'{key}' is not a target key in {source.Description}. " +
                    $"Expected '<platform>' or '<platform>|<configuration>', and not '{PolicyTargetKey.UnstatedPlatform}'.",
                    source.FilePath,
                    LineOf(node),
                    jsonPath));
                continue;
            }

            if (seen.TryGetValue(key, out var first))
            {
                errors.Add(new PolicyValidationError(
                    $"Target keys '{first}' and '{key}' differ only in case in {source.Description}, " +
                    "so both would apply and neither would win predictably.",
                    source.FilePath,
                    LineOf(node),
                    jsonPath));
                continue;
            }

            seen[key] = key;

            if (node is not PolicyNode.ObjectNode block)
            {
                errors.Add(new PolicyValidationError(
                    $"Target '{key}' must be an object in {source.Description}.",
                    source.FilePath,
                    LineOf(node),
                    jsonPath));
                continue;
            }

            // The inside of a target is a root scope: the same keys, validated
            // by the same walk. That means 'targets' is itself an accepted key
            // here, and nothing below descends into it — so a targets block
            // nested inside a target block passes validation and is then never
            // applied, because only the root block is read when the layer
            // resolves. It is the same defect the unparseable key above is
            // refused for, and it is not refused. Adding that refusal would
            // reject a document accepted today, so it is a change of contract
            // rather than a correction.
            ValidateScope(block, PolicyKeySchema.RootKeys, source, $"{jsonPath}.", errors);

            if (block.Members.GetValueOrDefault("rules") is PolicyNode.ObjectNode rules)
            {
                ValidateRuleMap(rules, knownRuleIds, source, errors);
            }
        }
    }

    private static void ValidateRuleMap(
        PolicyNode.ObjectNode rules, string[] knownRuleIds, ValidationSource source, List<PolicyValidationError> errors)
    {
        foreach (var (ruleId, ruleNode) in rules.Members)
        {
            if (!knownRuleIds.Contains(ruleId, StringComparer.Ordinal))
            {
                errors.Add(new PolicyValidationError(
                    Suggested($"Unknown rule id '{ruleId}' in {source.Description}.", ruleId, knownRuleIds),
                    source.FilePath,
                    LineOf(ruleNode),
                    $"rules.{ruleId}"));
                continue;
            }

            if (ruleNode is PolicyNode.ObjectNode ruleObject)
            {
                ValidateScope(ruleObject, PolicyKeySchema.RuleKeys, source, $"rules.{ruleId}.", errors);
                continue;
            }

            // Without this, a rule entry that is not an object — "rules": { "core.a.b": 42 }
            // — passed validation in silence, and the merge then replaced the
            // rule's whole subtree with that scalar, so the failure surfaced
            // later as an exception while reading an effective value. Section
            // 6.4: failing late in a validation tool is embarrassing, failing
            // silently is worse.
            errors.Add(new PolicyValidationError(
                $"Rule '{ruleId}' must be an object in {source.Description}.",
                source.FilePath,
                LineOf(ruleNode),
                $"rules.{ruleId}"));
        }
    }

    /// <summary>
    /// Walks one object against one key table: every member's name must be in
    /// the table, and every member's value must match that key's declared kind.
    /// </summary>
    private static void ValidateScope(
        PolicyNode.ObjectNode scope,
        FrozenDictionary<string, PolicyKeyDefinition> table,
        ValidationSource source,
        string jsonPathPrefix,
        List<PolicyValidationError> errors)
    {
        foreach (var (key, node) in scope.Members)
        {
            var jsonPath = jsonPathPrefix + key;

            if (!table.TryGetValue(key, out var definition))
            {
                errors.Add(new PolicyValidationError(
                    Suggested($"Unknown key '{key}' in {source.Description}.", key, table.Keys),
                    source.FilePath,
                    LineOf(node),
                    jsonPath));
                continue;
            }

            if (ValueError(definition, node) is { } message)
            {
                errors.Add(new PolicyValidationError(
                    $"{message} in {source.Description}.", source.FilePath, LineOf(node), jsonPath));
                continue;
            }

            if (definition.Kind is PolicyValueKind.VersionRange && node is PolicyNode.ObjectNode range)
            {
                ValidateVersionRange(range, definition, source, jsonPath, errors);
            }
        }
    }

    /// <summary>
    /// Walks a version range: known members only, and the lower bound is not
    /// optional.
    /// </summary>
    /// <remarks>
    /// The lower bound is required because a range open below says "any version
    /// ever published", which is not a bound and is indistinguishable from
    /// having written no key at all. The upper bound is optional and exclusive,
    /// the convention the workspace manifest already uses for a tool's version
    /// range.
    /// </remarks>
    private static void ValidateVersionRange(
        PolicyNode.ObjectNode range,
        PolicyKeyDefinition definition,
        ValidationSource source,
        string jsonPath,
        List<PolicyValidationError> errors)
    {
        ValidateScope(range, PolicyKeySchema.VersionRangeKeys, source, $"{jsonPath}.", errors);

        if (!range.Members.ContainsKey("minimumVersion"))
        {
            errors.Add(new PolicyValidationError(
                $"'{definition.Name}' needs 'minimumVersion' in {source.Description}.",
                source.FilePath,
                LineOf(range),
                $"{jsonPath}.minimumVersion"));
        }
    }

    /// <summary>
    /// Returns the problem with a value, or <see langword="null"/> when it
    /// matches its key's declared kind.
    /// </summary>
    private static string? ValueError(PolicyKeyDefinition definition, PolicyNode node)
    {
        // Opaque is the carve-out for settings — but that carve-out
        // is about what is *inside* settings, not about whether settings is an
        // object at all. Exempting the container too let "settings": 42 pass,
        // after which the merge replaced the subtree with the scalar and every
        // GetValue the rule made quietly returned its own fallback.
        if (definition.Kind is PolicyValueKind.Opaque or PolicyValueKind.RuleMap
            or PolicyValueKind.TargetMap or PolicyValueKind.VersionRange)
        {
            return node is PolicyNode.ObjectNode ? null : $"'{definition.Name}' must be an object";
        }

        if (node is not PolicyNode.Leaf leaf)
        {
            return $"'{definition.Name}' must be a single value, not an object";
        }

        return definition.Kind switch
        {
            PolicyValueKind.Boolean when leaf.Value.Value is not bool =>
                $"'{definition.Name}' must be a boolean",
            PolicyValueKind.Integer when leaf.Value.Value is not long =>
                $"'{definition.Name}' must be an integer",
            PolicyValueKind.Integer when definition.Range is { } range && leaf.Value.Value is long number &&
                (number < range.Minimum || number > range.Maximum) =>
                $"'{definition.Name}' must be between {range.Minimum} and {range.Maximum}",
            PolicyValueKind.String when leaf.Value.Value is not string =>
                $"'{definition.Name}' must be a string",
            PolicyValueKind.StringEnum when leaf.Value.Value is not string text ||
                !definition.AllowedValues!.Contains(text, StringComparer.Ordinal) =>
                $"'{definition.Name}' must be one of {string.Join(", ", definition.AllowedValues!)}",
            _ => null,
        };
    }

    private static string Suggested(string message, string input, IEnumerable<string> candidates)
    {
        var suggestions = SuggestionFinder.FindClosest(input, candidates);

        return suggestions.Count == 0
            ? message
            : $"{message} Did you mean '{string.Join("' or '", suggestions)}'?";
    }

    /// <summary>
    /// The line a node came from, when it came from a file at all.
    /// </summary>
    /// <remarks>
    /// Excluded from coverage rather than tested into the green. The
    /// non-<see cref="PolicyOrigin.FromFile"/> branch is unreachable from
    /// either caller: this type only ever inspects freshly parsed documents,
    /// and <c>PolicyDocument.Parse</c> stamps every leaf it creates with
    /// <see cref="PolicyOrigin.FromFile"/>. Origins of any other kind are
    /// minted later, inside <c>EffectivePolicy</c>, whose output is never fed
    /// back here. The signature stays deliberately general anyway — narrowing
    /// it purely to chase the metric would make the helper less reusable for no
    /// behavioural gain.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    private static int? LineOf(PolicyNode node) =>
        node is PolicyNode.Leaf { Value.Origin: PolicyOrigin.FromFile file } ? file.Line : null;
}
