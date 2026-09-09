namespace Preflight.Rules;

using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;

/// <summary>
/// Runs a version control client in the workspace root and says which of three
/// things happened.
/// </summary>
/// <remarks>
/// <para>
/// A collaborator two rules hold, not a base class they extend. They agree
/// exactly on how a client is invoked — in the workspace root, with an argument
/// list rather than a command line, and with a launch failure meaning the
/// client is absent rather than the rule being broken — and disagree on
/// everything downstream: which question to ask, and what an absent client
/// means for the workspace. A base class would own the whole execution and hand
/// each rule a template, which is a larger promise than they share, and an
/// external plugin author has no base class at all.
/// </para>
/// <para>
/// It lives apart from both stages' folders because it serves rules in two of
/// them, and putting it inside one would say it belongs to that one.
/// </para>
/// <para>
/// Internal, and it stays internal. A rule shipped in a plugin cannot reach it
/// and would write its own; a built-in rule using a facility no plugin can have
/// would be a built-in rule the contract cannot describe.
/// </para>
/// </remarks>
internal static class GitCommand
{
    /// <summary>
    /// The client asked when the workspace declares none.
    /// </summary>
    /// <remarks>
    /// A default rather than a literal inside each rule, because which client a
    /// repository uses is a fact the workspace states and the tool asks
    /// everywhere else. A production on another client declares it once.
    /// </remarks>
    public const string DefaultCommand = "git";

    /// <summary>
    /// Asks the client a question.
    /// </summary>
    /// <param name="context">The rule's context, for the runner and the root.</param>
    /// <param name="command">The client to run.</param>
    /// <param name="arguments">What to ask it.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Which of the three answers came back.</returns>
    /// <exception cref="OperationCanceledException">
    /// The run was cancelled. Deliberately not caught: a deadline that expired
    /// is the tool's verdict to give, and a rule that swallowed it would report
    /// the workspace as broken when what happened is that a command ran long.
    /// </exception>
    public static async Task<GitCommandResult> RunAsync(
        RuleContext context,
        string command,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(arguments);

        ProcessResult result;

        try
        {
            result = await context.Processes.RunAsync(
                new ProcessRequest
                {
                    FileName = command,
                    Arguments = arguments,

                    // The workspace root, always. Without it the client answers
                    // about whatever directory the tool was started in, which
                    // on a build machine is not the checkout being validated.
                    WorkingDirectory = context.WorkspaceRoot.FullName,
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Every other way of failing to start a process means the client is
            // not there, which is a fact about the machine rather than a defect
            // in the rule — so it is never an errored rule.
            return GitCommandResult.Unavailable();
        }

        // Standard output verbatim. Trimming here would be this collaborator
        // deciding what whitespace means for two questions that answer it
        // differently: a configuration value is one line whose surrounding
        // whitespace is noise, and a submodule status line begins with a space
        // when the submodule is up to date — so a shared trim would turn every
        // healthy submodule into a line the tool could not read.
        return result.ExitCode == 0
            ? GitCommandResult.Output(result.StandardOutput)
            : GitCommandResult.Failed(result.ExitCode);
    }

    /// <summary>
    /// Whether the workspace is a checkout of the declared client at all.
    /// </summary>
    /// <param name="context">The rule's context.</param>
    /// <param name="command">The client to ask.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>
    /// <see langword="true"/> when the client recognises the workspace as one
    /// of its repositories.
    /// </returns>
    /// <remarks>
    /// One shared question rather than each rule deriving the fact from the
    /// exit code of whatever it happened to ask. Outside a repository, asking
    /// for a configuration value exits non-zero with nothing on standard error,
    /// which is indistinguishable from a setting that is simply not there — so
    /// a rule deriving the fact from its own command would report a missing
    /// setting in a directory that was never a checkout. Asked here, the two
    /// rules that care get the same answer to the same question and are then
    /// free to disagree about what it means.
    /// </remarks>
    public static async Task<bool> IsRepositoryAsync(
        RuleContext context,
        string command,
        CancellationToken cancellationToken) =>
        (await RunAsync(context, command, ["rev-parse", "--git-dir"], cancellationToken))
            .Match(unavailable: () => false, failed: _ => false, output: _ => true);
}
