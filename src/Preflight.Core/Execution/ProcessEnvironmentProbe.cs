namespace Preflight.Core.Execution;

using Preflight.Abstractions.Services;

/// <summary>
/// The environment probe the tool ships, reading the real process block.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than in the command line for the same reason the file system,
/// the process runner and the volume probe are: the integration layer of the
/// rule tests needs the shipped implementation, and a test project cannot
/// reference an executable.
/// </para>
/// <para>
/// It asks the platform for each name rather than copying the block into a
/// dictionary at construction. The copy was the first shape and it was wrong
/// twice over. Name lookup has to match the shell that launched the build —
/// case-insensitive on Windows, case-sensitive elsewhere — and the dictionary
/// the runtime hands back compares ordinally on every platform, so the copy had
/// to choose a comparer with a test of the operating system. That is a branch
/// no single machine can take both sides of, which is a permanent hole in the
/// count for a snapshot that protects against a mutation nothing in this tool
/// performs: the one rule that reads the environment reads each name once, and
/// no rule can write one.
/// </para>
/// </remarks>
public sealed class ProcessEnvironmentProbe : IEnvironmentProbe
{
    /// <inheritdoc/>
    public string? Read(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Environment.GetEnvironmentVariable(name);
    }
}
