namespace Preflight.Cli.Storage;

using Preflight.Cli.Services;

/// <summary>
/// The real process environment.
/// </summary>
public sealed class ProcessEnvironmentReader : IEnvironmentReader
{
    public string? GetVariable(string name) => Environment.GetEnvironmentVariable(name);
}
