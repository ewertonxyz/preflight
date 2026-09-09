namespace Preflight.Rules;

/// <summary>
/// What one invocation of a version control client produced.
/// </summary>
/// <remarks>
/// <para>
/// Raw text and a shape, never a <see cref="Preflight.Abstractions.Model.Finding"/>.
/// Three rules ask a client three different questions and have to say
/// different things about the same answer: a client that is not installed is a
/// missing requirement to one of them and nothing at all to another. A
/// collaborator that returned findings would be choosing all three rules'
/// words, which is the one thing it must not do.
/// </para>
/// <para>
/// The variants are folded by <see cref="Match{T}"/> rather than by a
/// <c>switch</c> at each call site. A closed set of three cannot be proved
/// exhaustive to the compiler, so every caller would carry a discard arm no
/// input can reach — a permanent hole in the count, repeated once per caller.
/// Here the fold is written once, its final <c>else</c> <em>is</em> the third
/// variant, and a test by reflection holds the hierarchy at three so a fourth
/// cannot arrive unnoticed.
/// </para>
/// </remarks>
internal abstract record GitCommandResult
{
    private GitCommandResult()
    {
    }

    /// <summary>The client could not be run at all.</summary>
    /// <remarks>
    /// Detail is deliberately not carried. What the launch failure said names
    /// an absolute path on the machine that ran it, and every string a finding
    /// carries reaches a build log far more people read than ran the build.
    /// </remarks>
    public static GitCommandResult Unavailable() => new UnavailableResult();

    /// <summary>The client ran and exited non-zero.</summary>
    /// <param name="exitCode">What it exited with.</param>
    public static GitCommandResult Failed(int exitCode) => new FailedResult(exitCode);

    /// <summary>The client ran, exited zero, and printed <paramref name="text"/>.</summary>
    /// <param name="text">Standard output, exactly as the client wrote it.</param>
    public static GitCommandResult Output(string text) => new OutputResult(text);

    /// <summary>
    /// Folds the three variants into one value.
    /// </summary>
    /// <typeparam name="T">What the caller wants back.</typeparam>
    /// <param name="unavailable">For a client that could not be run.</param>
    /// <param name="failed">For a non-zero exit, given the code.</param>
    /// <param name="output">For a successful run, given what it printed.</param>
    /// <returns>Whichever of the three produced.</returns>
    public T Match<T>(Func<T> unavailable, Func<int, T> failed, Func<string, T> output)
    {
        ArgumentNullException.ThrowIfNull(unavailable);
        ArgumentNullException.ThrowIfNull(failed);
        ArgumentNullException.ThrowIfNull(output);

        // An if/else chain whose final else is the third variant, rather than a
        // switch with a discard. The discard would be unreachable and permanent.
        if (this is UnavailableResult)
        {
            return unavailable();
        }

        return this is FailedResult failure
            ? failed(failure.ExitCode)
            : output(((OutputResult)this).Text);
    }

    private sealed record UnavailableResult : GitCommandResult;

    private sealed record FailedResult(int ExitCode) : GitCommandResult;

    private sealed record OutputResult(string Text) : GitCommandResult;
}
