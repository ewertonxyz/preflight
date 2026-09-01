namespace Preflight.Cli.Storage;

using System.IO.Compression;
using Preflight.Core;

/// <summary>
/// Raised when a package archive cannot be used.
/// </summary>
public sealed class PackageArchiveException : ConfigurationLoadException
{
    public PackageArchiveException(string message)
        : base(message)
    {
    }
}
