namespace Preflight.Cli.Storage;

using System.Text.Json;
using Preflight.Core;

/// <summary>
/// Raised when an installed package's manifest cannot be read.
/// </summary>
public sealed class PackageManifestException : ConfigurationLoadException
{
    public PackageManifestException(string message)
        : base(message)
    {
    }
}
