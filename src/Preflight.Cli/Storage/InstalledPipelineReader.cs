namespace Preflight.Cli.Storage;

using System.Text.Json;
using Preflight.Cli.Pipelines;
using Preflight.Cli.Services;
using Preflight.Core;

/// <summary>
/// Reads the install root off the real disk.
/// </summary>
/// <remarks>
/// A directory whose name is not a package version is skipped rather than
/// raised, and so is one holding no manifest. The install root is a place a
/// person can open in a file manager, and a stray folder there must not stop a
/// run that was never going to read it — the same judgement
/// <c>PipelineSelector</c> makes about a stray <c>preflight.*.json</c> beside
/// the executable.
/// </remarks>
public sealed class InstalledPipelineReader : IInstalledPipelineReader
{
    private readonly PipelineInstallRoot _root;

    public InstalledPipelineReader(PipelineInstallRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);

        _root = root;
    }

    /// <inheritdoc />
    public IReadOnlyList<PackageVersion> Versions(string pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        var directory = _root.PipelineDirectory(pipeline);

        if (!directory.Exists)
        {
            return [];
        }

        return
        [
            .. directory
                .EnumerateDirectories()
                .Select(candidate =>
                    PackageVersion.TryParse(candidate.Name, out var version) ? version : null)
                .OfType<PackageVersion>()
                .Where(version => File.Exists(
                    Path.Combine(
                        _root.VersionDirectory(pipeline, version).FullName, PackageManifest.FileName)))
                .Order(),
        ];
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Pipelines()
    {
        var pipelines = new DirectoryInfo(Path.Combine(_root.Root.FullName, "pipelines"));

        if (!pipelines.Exists)
        {
            return [];
        }

        return
        [
            .. pipelines
                .EnumerateDirectories()
                .Select(directory => directory.Name)
                .Where(PipelineName.IsValid)
                .Where(name => Versions(name).Count > 0)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <inheritdoc />
    public PackageManifest Manifest(string pipeline, PackageVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var path = Path.Combine(
            _root.VersionDirectory(pipeline, version).FullName, PackageManifest.FileName);

        return Read(path);
    }

    /// <summary>
    /// Reads and validates one manifest from an absolute path.
    /// </summary>
    /// <remarks>
    /// An unknown <c>schemaVersion</c> is refused rather than read for the parts
    /// this binary recognises. That is how the policy schema already treats an
    /// unknown version, applied to a second format: a manifest
    /// written for a newer schema can express intentions this binary cannot
    /// honour, and installing what it understood would put a weaker pipeline on
    /// disk than its author published.
    /// </remarks>
    /// <param name="path">The manifest's location.</param>
    /// <exception cref="PackageManifestException">It is absent, malformed or too new.</exception>
    public static PackageManifest Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            throw new PackageManifestException($"No package manifest at {path}.");
        }

        ManifestDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<ManifestDocument>(
                File.ReadAllText(path), ManifestSerialization.Options);
        }
        catch (Exception exception) when (exception is JsonException or IOException
            or UnauthorizedAccessException)
        {
            throw new PackageManifestException($"Could not read {path}: {exception.Message}");
        }

        if (document is null)
        {
            throw new PackageManifestException($"The package manifest at {path} is empty.");
        }

        if (document.SchemaVersion != PackageManifest.CurrentSchemaVersion)
        {
            throw new PackageManifestException(
                $"The package manifest at {path} declares schemaVersion " +
                $"{document.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
                $"and this build understands " +
                $"{PackageManifest.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)}. " +
                "Upgrade preflight.");
        }

        if (document.Name is not { } name || !PipelineName.IsValid(name))
        {
            throw new PackageManifestException(
                $"The package manifest at {path} does not name a pipeline.");
        }

        if (!PackageVersion.TryParse(document.Version, out var version))
        {
            throw new PackageManifestException(
                $"'{document.Version}' in {path} is not a package version.");
        }

        if (document.PolicyFile is not { } policyFile || policyFile.Length == 0)
        {
            throw new PackageManifestException(
                $"The package manifest at {path} does not name a policy file.");
        }

        if (document.AbstractionsMinimumVersion is not { } minimum || minimum.Length == 0)
        {
            throw new PackageManifestException(
                $"The package manifest at {path} does not declare the contract version its rules need.");
        }

        return new PackageManifest(
            document.SchemaVersion,
            name,
            version!,
            policyFile,
            document.RuleAssemblies ?? [],
            minimum,
            document.AbstractionsMaximumVersion,
            document.Sha256ByRelativePath ?? new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private sealed record ManifestDocument
    {
        public int SchemaVersion { get; init; }

        public string? Name { get; init; }

        public string? Version { get; init; }

        public string? PolicyFile { get; init; }

        public IReadOnlyList<string>? RuleAssemblies { get; init; }

        public string? AbstractionsMinimumVersion { get; init; }

        public string? AbstractionsMaximumVersion { get; init; }

        public Dictionary<string, string>? Sha256ByRelativePath { get; init; }
    }
}
