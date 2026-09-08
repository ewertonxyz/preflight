namespace Preflight.Rules;

/// <summary>
/// What came back from asking a tool for its version.
/// </summary>
/// <remarks>
/// Three answers rather than two, because "the tool is not there" and "the tool
/// is there and printed something this cannot read" are different problems with
/// different remedies — one is an install, the other is a command that prints
/// the wrong thing. Folding them together would hand the reader one sentence
/// for two situations.
/// </remarks>
internal enum ToolProbeStatus
{
    /// <summary>The tool ran and printed a version.</summary>
    Found,

    /// <summary>The tool could not be run, or ran and failed.</summary>
    Unavailable,

    /// <summary>The tool ran and printed nothing a version could be read from.</summary>
    Unreadable,
}
