namespace Preflight.Abstractions.Services;

/// <summary>
/// What one process run produced.
/// </summary>
/// <remarks>
/// Both streams are carried separately rather than interleaved. A probe that
/// failed usually explains itself on one of the two, and a rule that has to
/// split a merged transcript back apart is a rule that will get it wrong on the
/// first tool that writes progress to standard error.
/// </remarks>
public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration);
