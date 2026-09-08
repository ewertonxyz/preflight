namespace Preflight.Rules;

/// <summary>
/// What <see cref="ToolProbe"/> learned about one tool.
/// </summary>
/// <param name="Status">Which of the three answers this is.</param>
/// <param name="Version">
/// The version read, when <paramref name="Status"/> is
/// <see cref="ToolProbeStatus.Found"/>; otherwise <see langword="null"/>.
/// </param>
/// <param name="Detail">
/// What the tool said — the launch failure, the standard error of a failed run,
/// or the output no version could be read from. Empty when there was nothing.
/// </param>
/// <remarks>
/// Raw text and a status, never a <see cref="Preflight.Abstractions.Model.Finding"/>.
/// Two rules ask a tool for its version and need to say different things about
/// the same answer: one is reporting on a toolchain the workspace declared, the
/// other on a platform SDK the policy asked for, and the remedy differs. A
/// collaborator that returned findings would be choosing both rules' words,
/// which is the one thing it must not do — and truncation belongs to the caller
/// for the same reason, since how much of a tool's output is worth showing
/// depends on what the reader is being asked to fix.
/// </remarks>
internal sealed record ToolProbeResult(ToolProbeStatus Status, Version? Version, string Detail)
{
    /// <summary>The tool ran and printed <paramref name="version"/>.</summary>
    public static ToolProbeResult Found(Version version) =>
        new(ToolProbeStatus.Found, version, string.Empty);

    /// <summary>
    /// The tool could not be run, or ran and failed, saying
    /// <paramref name="detail"/>.
    /// </summary>
    public static ToolProbeResult Unavailable(string detail) =>
        new(ToolProbeStatus.Unavailable, Version: null, detail);

    /// <summary>
    /// The tool ran and printed <paramref name="output"/>, which carries no
    /// version.
    /// </summary>
    public static ToolProbeResult Unreadable(string output) =>
        new(ToolProbeStatus.Unreadable, Version: null, output);
}
