namespace Preflight.Cli.Storage;

using System.IO.Compression;
using Preflight.Core;

/// <summary>One file inside a package archive.</summary>
/// <param name="RelativePath">Its path inside the package, with forward slashes.</param>
public sealed record PackageEntry(string RelativePath);
