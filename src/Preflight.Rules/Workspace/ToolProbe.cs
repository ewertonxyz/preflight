namespace Preflight.Rules;

using System.Text.RegularExpressions;
using Preflight.Abstractions.Services;

/// <summary>
/// Runs a tool, reads the version out of whatever it printed, and says which of
/// the three things happened.
/// </summary>
/// <remarks>
/// <para>
/// A collaborator two rules hold, not a base class they extend. They agree
/// exactly on how a tool is run and on what counts as a version, and disagree on
/// everything downstream of that: which tool to ask, what a bad answer means,
/// and what the reader should do about it. A base class would own the whole
/// execution and hand each rule a template, which is a larger promise than they
/// share — and an external plugin author has no base class at all, so a built-in
/// rule leaning on one would be written against a facility the plugin model does
/// not offer.
/// </para>
/// <para>
/// Internal, and it stays internal. A rule shipped in a plugin cannot reach it
/// and would write its own; a built-in rule using a facility no plugin can have
/// would be a built-in rule the contract cannot describe.
/// </para>
/// </remarks>
internal static partial class ToolProbe
{
    /// <summary>
    /// The leading numeric run of a version-looking token, at most four
    /// components long.
    /// </summary>
    /// <remarks>
    /// Four, because <see cref="Version"/> holds four and a fifth makes
    /// <see cref="Version.TryParse(string, out Version)"/> fail. A tool that
    /// prints five is not describing something this comparison needs to
    /// distinguish.
    ///
    /// Generated at compile time rather than built with
    /// <see cref="RegexOptions.Compiled"/>. That option emits IL on the first
    /// match, and this process lives for seconds against one match per declared
    /// tool — a cost that never amortises. The generator pays it at build time
    /// instead.
    /// </remarks>
    [GeneratedRegex(@"^\d+(\.\d+){0,3}", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingVersion { get; }

    /// <summary>
    /// Asks a tool for its version.
    /// </summary>
    /// <param name="processes">How the child process is started.</param>
    /// <param name="request">The command, its arguments, and where to run it.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Which of the three answers came back, and the text behind it.</returns>
    /// <exception cref="OperationCanceledException">
    /// The run was cancelled. Deliberately not caught: a deadline that expired
    /// is the tool's verdict to give, and a rule that swallowed it would report
    /// the workspace as broken when what happened is that a command ran long.
    /// </exception>
    public static async Task<ToolProbeResult> RunAsync(
        IProcessRunner processes,
        ProcessRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(request);

        ProcessResult result;

        try
        {
            result = await processes.RunAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Every other way starting a process fails means the tool is not
            // installed, which is what both callers exist to say.
            return ToolProbeResult.Unavailable(exception.Message);
        }

        if (result.ExitCode != 0)
        {
            return ToolProbeResult.Unavailable(result.StandardError.Trim());
        }

        return ParseVersion(result.StandardOutput) is { } version
            ? ToolProbeResult.Found(version)
            : ToolProbeResult.Unreadable(result.StandardOutput);
    }

    /// <summary>
    /// Reads a version out of whatever the tool printed.
    /// </summary>
    /// <param name="output">Everything the tool wrote to standard output.</param>
    /// <returns>The version, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// <para>
    /// The leading numeric run of the first number-looking token on the first
    /// line, capped at four components. Real tools do not print bare versions:
    /// <c>dotnet --version</c> gives <c>10.0.100</c>, a preview install gives
    /// <c>10.0.100-preview.3.25</c>, and <c>git --version</c> on Windows gives
    /// <c>git version 2.51.0.windows.1</c> — five components, the last two of
    /// which are not numbers at all.
    /// </para>
    /// <para>
    /// Taking the leading run rather than the whole token is what makes all
    /// three readable. The alternative, refusing anything that is not exactly a
    /// version, reports a machine that has the tool as having none — and this
    /// was found by a fixture, not by inspection.
    /// </para>
    /// </remarks>
    public static Version? ParseVersion(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();

        if (string.IsNullOrEmpty(firstLine))
        {
            return null;
        }

        var token = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(candidate => char.IsDigit(candidate[0]));

        if (token is null)
        {
            return null;
        }

        // Match, not TryMatch-and-check. The token was selected because its
        // first character is a digit, and the pattern needs exactly one — so a
        // failed match is a state no input can produce, and testing for it
        // would be a branch nothing can take.
        var leading = LeadingVersion.Match(token).Value;

        return Version.TryParse(leading, out var version) ? version : null;
    }
}
