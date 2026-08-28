namespace Preflight.Cli.Tests.Commands;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Preflight.TestSupport;

/// <summary>
/// Builds real pipeline packages for the install tests.
/// </summary>
/// <remarks>
/// Real archives rather than a substituted one, because what
/// <c>PipelineInstaller</c> does is decide whether an archive may be trusted,
/// and a substitute that always agreed with the manifest would leave every
/// refusal untested. The seam exists for the cases that cannot be built here —
/// an entry name a zip writer will not produce — not for these.
/// </remarks>
public static class PackageFixtures
{
    /// <summary>Writes a package holding a policy file and nothing else.</summary>
    /// <param name="directory">Where to write it.</param>
    /// <param name="name">The pipeline name.</param>
    /// <param name="version">The package version.</param>
    /// <param name="policy">The policy document's content.</param>
    /// <param name="contractMinimum">The contract version its rules need.</param>
    /// <param name="corrupt">How to damage it, for the refusal tests.</param>
    public static string Write(
        DirectoryInfo directory,
        string name,
        string version,
        string? policy = null,
        string? contractMinimum = null,
        PackageDamage corrupt = PackageDamage.None)
    {
        ArgumentNullException.ThrowIfNull(directory);

        var policyFileName = $"preflight.{name}.json";
        var policyContent = policy ?? """{ "schemaVersion": 1 }""";
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [policyFileName] = Encoding.UTF8.GetBytes(policyContent),
        };

        var digests = files.ToDictionary(
            file => file.Key,
            file => Convert.ToHexString(SHA256.HashData(file.Value)),
            StringComparer.Ordinal);

        if (corrupt is PackageDamage.DigestMismatch)
        {
            digests[policyFileName] = new string('0', 64);
        }

        if (corrupt is PackageDamage.MissingFromArchive)
        {
            digests["rules/Absent.dll"] = new string('0', 64);
        }

        if (corrupt is PackageDamage.UnlistedFile)
        {
            files["rules/Unlisted.dll"] = [1, 2, 3];
        }

        var manifest = new
        {
            schemaVersion = corrupt is PackageDamage.UnknownSchema ? 99 : 1,
            name,
            version,
            policyFile = policyFileName,
            ruleAssemblies = Array.Empty<string>(),
            abstractionsMinimumVersion = contractMinimum ?? ContractVersion.Current,
            sha256ByRelativePath = digests,
        };

        var manifestBytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(manifest, ManifestSerialization.Options));

        var path = Path.Combine(directory.FullName, $"{name}-{version}.zip");

        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            Add(archive, PackageManifest.FileName, manifestBytes);

            foreach (var (relative, content) in files.OrderBy(file => file.Key, StringComparer.Ordinal))
            {
                Add(archive, relative, content);
            }
        }

        return path;
    }

    private static void Add(ZipArchive archive, string relativePath, byte[] content)
    {
        using var entry = archive.CreateEntry(relativePath).Open();

        entry.Write(content);
    }
}

/// <summary>The ways a package fixture can be damaged.</summary>
public enum PackageDamage
{
    /// <summary>A package that installs.</summary>
    None,

    /// <summary>A file whose digest does not match the manifest.</summary>
    DigestMismatch,

    /// <summary>A file the manifest lists and the archive lacks.</summary>
    MissingFromArchive,

    /// <summary>A file the archive carries and the manifest does not list.</summary>
    UnlistedFile,

    /// <summary>A manifest schema this build does not understand.</summary>
    UnknownSchema,
}
