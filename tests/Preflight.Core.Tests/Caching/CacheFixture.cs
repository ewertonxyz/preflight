namespace Preflight.Core.Tests.Caching;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Core;
using Preflight.Core.Caching;
using Preflight.Core.Execution;
using Preflight.Core.Policy;
using Preflight.Core.Tests.Execution;
using static Preflight.Core.Tests.Graph.GraphFixture;

/// <summary>
/// A rule that can describe its inputs, or refuses to, or breaks trying.
/// </summary>
/// <remarks>
/// Hand-written rather than substituted for the reason <c>FakeRule</c> gives:
/// these tests need a rule that blocks until the test lets it go and that
/// observes the token it was handed, and a mocking library says neither
/// clearly.
/// </remarks>
internal sealed class FakeCacheableRule : IValidationRule, ICacheableRule
{
    private readonly Func<CancellationToken, Task<CacheFingerprint?>> _fingerprint;
    private readonly Func<RuleOutcome> _outcome;

    private FakeCacheableRule(
        RuleDescriptor descriptor,
        Func<CancellationToken, Task<CacheFingerprint?>> fingerprint,
        Func<RuleOutcome> outcome)
    {
        Descriptor = descriptor;
        _fingerprint = fingerprint;
        _outcome = outcome;
    }

    public RuleDescriptor Descriptor { get; }

    public int Executions { get; private set; }

    public int Fingerprints { get; private set; }

    /// <summary>Completes the moment the fingerprint is entered.</summary>
    /// <remarks>
    /// The same substitution point <c>FakeRule.Started</c> is, and for the same
    /// reason: a
    /// cancellation test has to cancel <em>while</em> the rule is inside the
    /// call, and sleeping until it probably is would be a race dressed as a
    /// test.
    /// </remarks>
    public TaskCompletionSource FingerprintStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        Executions++;

        return Task.FromResult(_outcome());
    }

    public Task<CacheFingerprint?> ComputeFingerprintAsync(
        RuleContext context,
        CancellationToken cancellationToken)
    {
        Fingerprints++;
        FingerprintStarted.TrySetResult();

        return _fingerprint(cancellationToken);
    }

    /// <summary>A rule that describes its inputs and passes.</summary>
    public static FakeCacheableRule Describing(
        string id,
        string fingerprint = "aaaa",
        RuleOutcome? outcome = null,
        params string[] dependsOn) =>
        new(
            Rule(id, dependsOn),
            _ => Task.FromResult<CacheFingerprint?>(new CacheFingerprint(fingerprint)),
            () => outcome ?? RuleOutcome.Passed());

    /// <summary>A rule that declines, per the fingerprint contract.</summary>
    public static FakeCacheableRule Declining(string id) =>
        new(Rule(id), _ => Task.FromResult<CacheFingerprint?>(null), RuleOutcome.Passed);

    /// <summary>A rule whose fingerprint throws.</summary>
    public static FakeCacheableRule Breaking(string id, string message) =>
        new(Rule(id), _ => throw new InvalidOperationException(message), RuleOutcome.Passed);

    /// <summary>A rule whose fingerprint never finishes on its own.</summary>
    public static FakeCacheableRule Hanging(string id) =>
        new(
            Rule(id),
            async token =>
            {
                await Task.Delay(Timeout.Infinite, token);

                return null;
            },
            RuleOutcome.Passed);
}

/// <summary>
/// An <see cref="IRuleCacheStore"/> in a dictionary, with optional failure.
/// </summary>
internal sealed class RecordingCacheStore : IRuleCacheStore
{
    private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);

    public RecordingCacheStore(Exception? failure = null)
    {
        Failure = failure;
    }

    public Exception? Failure { get; }

    public IReadOnlyDictionary<string, string> Entries => _entries;

    public int Writes { get; private set; }

    public int Reads { get; private set; }

    public Task<string?> ReadAsync(string filePath, CancellationToken cancellationToken)
    {
        Reads++;

        return Failure is not null
            ? Task.FromException<string?>(Failure)
            : Task.FromResult(_entries.GetValueOrDefault(filePath));
    }

    public Task WriteAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        Writes++;

        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        _entries[filePath] = content;

        return Task.CompletedTask;
    }

    public int Clear(string directory)
    {
        var count = _entries.Count;

        _entries.Clear();

        return count;
    }

    /// <summary>Seeds the store as if a previous run had written this.</summary>
    public void Seed(string filePath, string content) => _entries[filePath] = content;
}

/// <summary>
/// The pieces a caching test needs around one rule.
/// </summary>
internal static class CacheFixture
{
    public static readonly DirectoryInfo Workspace = new(Path.Combine(Path.GetTempPath(), "preflight-cache-tests"));

    /// <summary>
    /// Where the history sits for the refusal tests: the engine default,
    /// resolved against <see cref="Workspace"/>.
    /// </summary>
    public static readonly string History = Path.Combine(Workspace.FullName, ".preflight", "history");

    public const string Directory = "/cache";

    public static EffectivePolicy PolicyFor(IValidationRule rule) =>
        EffectivePolicy.Build([rule.Descriptor], pipeline: null, local: null, setOverrides: [], target: StatedBuildTarget.Unstated);

    public static RuleCache CacheFor(IRuleCacheStore store, IValidationRule rule) =>
        new(store, Directory, PolicyFor(rule));

    public static RuleContext ContextFor(IValidationRule rule, RecordingRuleLoggerFactory loggers) => new()
    {
        WorkspaceRoot = Workspace,
        Stage = ValidationStage.BuildReadiness,
        Target = new BuildTarget("x64", "Debug"),
        ChangedFiles = [],
        Policy = PolicyFor(rule).ReaderFor(rule.Descriptor.Id),
        Logger = loggers.ForRule(rule.Descriptor.Id),
        FileSystem = Substitute.For<IFileSystem>(),
        Processes = Substitute.For<IProcessRunner>(),
    };

    public static RulePolicySnapshot SnapshotFor(IValidationRule rule, TimeSpan? timeout = null) => new()
    {
        RuleId = rule.Descriptor.Id,
        Enabled = true,
        Blocking = true,
        Gating = true,
        EffectiveSeverity = Severity.Error,
        Timeout = timeout ?? TimeSpan.FromSeconds(60),
    };
}
