namespace Preflight.Cli.Storage;

using System.Text.Json;
using Preflight.Core;

/// <summary>How a package manifest is written and read.</summary>
/// <remarks>
/// One options instance, shared by the reader and by <c>pipeline pack</c>, so
/// that the two cannot drift into writing and reading different shapes.
/// </remarks>
public static class ManifestSerialization
{
    /// <summary>Camel case in, camel case out, indented for a file people open.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}
