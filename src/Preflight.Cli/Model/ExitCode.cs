namespace Preflight.Cli.Model;

using Preflight.Core;

/// <summary>
/// Every exit code this tool produces, and the only place that decides which
/// one a run produces.
/// </summary>
/// <remarks>
/// <para>
/// The distinction 2 makes against 1 is the point of the table: a pipeline has
/// to treat broken configuration differently from rejected code, because the
/// first calls the tool's owner and the second calls the commit's author.
/// Getting that backwards is not a cosmetic defect — it routes an incident to
/// the wrong person, quietly, every time.
/// </para>
/// <para>
/// Which is also why this is a type and not four literals scattered through the
/// command handlers. A literal <c>return 2</c> written in one handler and
/// <c>return 1</c> in another is exactly how the distinction erodes.
/// </para>
/// </remarks>
public static class ExitCode
{
    /// <summary><c>Passed</c> or <c>PassedWithWarnings</c>.</summary>
    public const int Success = 0;

    /// <summary><c>Blocked</c>, including a warning promoted by <c>--fail-on-warning</c>.</summary>
    public const int Blocked = 1;

    /// <summary>
    /// Configuration error: invalid policy, a cycle in the graph, a dependency
    /// that does not exist, a plugin that would not load, or an invocation the
    /// CLI refuses rather than guesses at.
    /// </summary>
    public const int ConfigurationError = 2;

    /// <summary>Internal error, a rule in <c>Errored</c>, or a cancelled run.</summary>
    public const int InternalError = 3;

    /// <summary>
    /// <c>measure</c> could not start the child it was asked to time.
    /// </summary>
    /// <remarks>
    /// The shell convention for <em>command not found</em>, and deliberately
    /// not 2: 2 says the invocation of <c>preflight</c> is wrong and 127 says
    /// the command it was told to measure does not exist, and a pipeline that
    /// cannot tell them apart calls the tool's owner about somebody's typo. A
    /// legitimate child returning 127 is indistinguishable, which is already
    /// true in every shell and is what the number means.
    /// </remarks>
    public const int ChildNotStarted = 127;

    /// <summary>
    /// Maps a run's verdict to its exit code.
    /// </summary>
    /// <remarks>
    /// <c>Passed</c> and <c>PassedWithWarnings</c> deliberately collapse onto
    /// 0, and so do <c>Blocked</c> and a <c>--fail-on-warning</c> promotion
    /// onto 1. That means the exit code alone cannot tell a caller which of the
    /// two happened — <c>RunResult.FailOnWarning</c> is the surviving evidence,
    /// and confusing them overstates what the tool caught. A test asserting
    /// only the code proves less than it looks like it does.
    /// </remarks>
    public static int ForVerdict(RunVerdict verdict) => verdict switch
    {
        RunVerdict.Passed => Success,
        RunVerdict.PassedWithWarnings => Success,
        RunVerdict.Blocked => Blocked,
        RunVerdict.Errored => InternalError,
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Unmapped run verdict."),
    };

    /// <summary>
    /// Maps an exception that escaped a command to its exit code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One <c>catch</c> on <see cref="ConfigurationLoadException"/>, never one
    /// per subtype. That base exists — and says so in its own remarks — so that
    /// a fourth kind of configuration error added later cannot be silently
    /// missed by a caller that only knew about three.
    /// </para>
    /// <para>
    /// Cancellation is 3 and not a code of its own: a cancelled run is
    /// <c>Errored</c>, without an exception attached. The verdict already
    /// carries the finer distinction.
    /// </para>
    /// </remarks>
    public static int ForException(Exception exception) => exception switch
    {
        ConfigurationLoadException => ConfigurationError,
        OperationCanceledException => InternalError,
        _ => InternalError,
    };
}
