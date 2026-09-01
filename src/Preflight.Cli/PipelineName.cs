namespace Preflight.Cli;

using Preflight.Core.Policy;

/// <summary>
/// The one rule for what may be a pipeline name.
/// </summary>
/// <remarks>
/// <para>
/// A pipeline name becomes part of a file name and, since the install root
/// exists, part of a directory path outside the workspace. Left unchecked,
/// <c>../../etc/passwd</c> reads a file nobody meant to read and an empty name
/// reads <c>preflight..json</c> — neither of which is what the argument means.
/// </para>
/// <para>
/// Extracted here because the same condition was written twice before this
/// phase, and this phase opens four more doors that need it: <c>pipeline
/// declare</c>, <c>use</c>, <c>install</c> and <c>list</c>. The two callers keep
/// their own messages, and that is deliberate rather than an oversight — see
/// <see cref="Require"/>.
/// </para>
/// </remarks>
public static class PipelineName
{
    /// <summary>Whether <paramref name="name"/> may name a pipeline.</summary>
    /// <param name="name">The candidate.</param>
    public static bool IsValid(string? name) =>
        !string.IsNullOrEmpty(name) &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    /// <summary>
    /// Refuses <paramref name="name"/> unless it may name a pipeline.
    /// </summary>
    /// <remarks>
    /// <paramref name="filePath"/> decides the wording, and the two wordings
    /// stay distinct on purpose. A name typed at the command line needs no file
    /// named back at the person who just typed it; a name read out of a
    /// versioned file needs it as the first thing in the sentence, because
    /// <see cref="PolicyValidationException"/> composes its message from the
    /// messages alone and nobody typed that name today.
    /// </remarks>
    /// <param name="name">The candidate.</param>
    /// <param name="fileName">
    /// The file the name was read from, or <see langword="null"/> when it came
    /// from the command line.
    /// </param>
    /// <param name="filePath">The full path, for the error's own field.</param>
    /// <param name="key">The policy key the error is attributed to.</param>
    /// <exception cref="PolicyValidationException">The name is not a label.</exception>
    public static void Require(
        string name, string? fileName = null, string? filePath = null, string key = "pipeline")
    {
        ArgumentNullException.ThrowIfNull(name);

        if (IsValid(name))
        {
            return;
        }

        var origin = fileName is null ? string.Empty : $" in {fileName}";

        throw new PolicyValidationException([
            new PolicyValidationError(
                $"'{name}'{origin} is not a pipeline name. Expected letters, digits, '-' or '_'.",
                filePath,
                null,
                key),
        ]);
    }
}
