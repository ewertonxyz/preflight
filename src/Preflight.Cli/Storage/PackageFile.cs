namespace Preflight.Cli.Storage;

using System.IO.Compression;
using Preflight.Core;

/// <summary>One file on its way into a package archive.</summary>
/// <param name="RelativePath">Its path inside the package, with forward slashes.</param>
/// <param name="Content">Its bytes.</param>
public sealed record PackageFile(string RelativePath, byte[] Content);
