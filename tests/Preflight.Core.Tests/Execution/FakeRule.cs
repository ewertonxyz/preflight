namespace Preflight.Core.Tests.Execution;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using static Preflight.Core.Tests.Graph.GraphFixture;

/// <summary>
/// A rule whose behaviour the test dictates.
/// </summary>
/// <remarks>
/// Hand-written rather than substituted. the concurrency contract runs rules
/// in a level concurrently, so a fake used here is invoked from several threads
/// at once, and a mocking library records its calls in structures that were not
/// designed for that. It also has to do three things a substitute cannot say
/// clearly: block until the test releases it, observe the token it was handed,
/// and count overlapping entries.
/// </remarks>
internal sealed class FakeRule : IValidationRule
{
    private readonly Func<RuleContext, CancellationToken, Task<RuleOutcome>> _behaviour;

    private FakeRule(RuleDescriptor descriptor, Func<RuleContext, CancellationToken, Task<RuleOutcome>> behaviour)
    {
        Descriptor = descriptor;
        _behaviour = behaviour;
    }

    public RuleDescriptor Descriptor { get; }

    /// <summary>Completes the moment the rule is entered.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Invoked { get; private set; }

    public CancellationToken SeenToken { get; private set; }

    public RuleContext? SeenContext { get; private set; }

    public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        Invoked = true;
        SeenToken = cancellationToken;
        SeenContext = context;
        Started.TrySetResult();

        return _behaviour(context, cancellationToken);
    }

    public static FakeRule Passing(string id, params string[] dependsOn) =>
        Returning(id, RuleOutcome.Passed(), dependsOn);

    public static FakeRule Warning(string id, params string[] dependsOn) =>
        Returning(id, RuleOutcome.Warned(new Finding { Message = "warned" }), dependsOn);

    public static FakeRule Failing(string id, params string[] dependsOn) =>
        Returning(id, RuleOutcome.Failed(new Finding { Message = "failed" }), dependsOn);

    public static FakeRule NotApplicable(string id, params string[] dependsOn) =>
        Returning(id, RuleOutcome.NotApplicable(), dependsOn);

    public static FakeRule WithFindings(string id, params Finding[] findings) =>
        Returning(id, RuleOutcome.Warned(findings), []);

    /// <summary>A rule that declares a status only the tool may produce.</summary>
    public static FakeRule SelfDeclaring(string id, RuleStatus status) =>
        Returning(id, new RuleOutcome { Status = status }, []);

    public static FakeRule Throwing(string id, string message, params string[] dependsOn) =>
        new(Rule(id, dependsOn), (_, _) => throw new InvalidOperationException(message));

    /// <summary>
    /// Fails through a faulted task rather than by throwing at the call site.
    /// </summary>
    /// <remarks>
    /// A different runtime path from <see cref="Throwing"/>: that one throws
    /// before the runner ever holds a task, this one throws out of the await.
    /// Covering only one of the two leaves the other unguarded.
    /// </remarks>
    public static FakeRule ThrowingAsync(string id, string message) =>
        new(Rule(id), (_, _) => Task.FromException<RuleOutcome>(new InvalidOperationException(message)));

    /// <summary>Never completes on its own, but honours the token it is given.</summary>
    public static FakeRule Hanging(string id, params string[] dependsOn) =>
        new(Rule(id, dependsOn), async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return RuleOutcome.Passed();
        });

    /// <summary>
    /// Never completes and ignores the token — the contract violation of
    /// the concurrency contract that the runner has to survive rather than wait out.
    /// </summary>
    public static FakeRule Ignoring(string id, TaskCompletionSource<RuleOutcome> release, params string[] dependsOn) =>
        new(Rule(id, dependsOn), (_, _) => release.Task);

    /// <summary>Completes only when the test releases the gate.</summary>
    public static FakeRule Gated(string id, TaskCompletionSource gate, RuleOutcome outcome, params string[] dependsOn) =>
        new(Rule(id, dependsOn), async (_, _) =>
        {
            await gate.Task;
            return outcome;
        });

    /// <summary>
    /// Returns a task whose result is null — which the signature says cannot
    /// happen, but a plugin compiled without nullable analysis can still do.
    /// </summary>
    public static FakeRule ReturningNull(string id) =>
        new(Rule(id), (_, _) => Task.FromResult<RuleOutcome>(null!));

    /// <summary>Runs the supplied behaviour, for the cases none of the above fit.</summary>
    public static FakeRule Custom(
        string id, Func<RuleContext, CancellationToken, Task<RuleOutcome>> behaviour, params string[] dependsOn) =>
        new(Rule(id, dependsOn), behaviour);

    private static FakeRule Returning(string id, RuleOutcome outcome, string[] dependsOn) =>
        new(Rule(id, dependsOn), (_, _) => Task.FromResult(outcome));
}
