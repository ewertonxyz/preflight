namespace Preflight.Cli.Commands;

using System.Security.Cryptography;
using Preflight.Core.Plugins;

/// <summary>
/// <c>preflight pipeline install</c>, and the retention sweep that follows it.
/// </summary>
/// <remarks>
/// <para>
/// Everything is decided before a byte is written: the manifest is read, the
/// contract range is checked against this binary, and every file the archive
/// carries is matched against the digest the manifest claims for it. A package
/// that fails any of those is refused with nothing left on disk, because a
/// half-installed version that the resolver can see is a run with a partial rule
/// set reporting success.
/// </para>
/// <para>
/// The contract check happens here rather than only at load. That is not the
/// loader's check moved — the loader still makes it, and a test says so. It is
/// the same question asked at the moment one person can answer it, instead of on
/// a hundred machines at once.
/// </para>
/// <para>
/// This command never writes a pin. If it did, every delivery through a studio's
/// toolchain would move every machine's pin together, and the rollback the
/// retained versions exist for would stop existing. See ADR-032 and ADR-033.
/// </para>
/// </remarks>
public static class PipelineInstaller
{
    /// <summary>
    /// Installs one package from a local path.
    /// </summary>
    /// <param name="environment">Where the machine is.</param>
    /// <param name="packagePath">The archive to install.</param>
    /// <param name="keep">How many versions to retain, when the caller overrides it.</param>
    /// <param name="noGc">Whether to skip the retention sweep entirely.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static Task<int> InstallAsync(
        CommandEnvironment environment,
        string packagePath,
        int? keep,
        bool noGc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(packagePath);

        cancellationToken.ThrowIfCancellationRequested();

        var root = environment.InstallRoot;
        var archive = environment.PackageArchive;
        var full = Path.GetFullPath(packagePath);

        var manifest = ReadManifest(archive, full);

        RequireCompatibleContract(manifest, full);
        RequireEveryFileAccountedFor(archive, full, manifest);

        var destination = root.VersionDirectory(manifest.Name, manifest.Version);
        var staging = environment.InstallWriter.CreateStaging(root);

        try
        {
            foreach (var entry in archive.Entries(full))
            {
                cancellationToken.ThrowIfCancellationRequested();

                environment.InstallWriter.WriteStaged(
                    staging, entry.RelativePath, archive.Read(full, entry.RelativePath));
            }

            environment.InstallWriter.Commit(staging, destination);
        }
        catch
        {
            environment.InstallWriter.Remove(staging);

            throw;
        }

        environment.Console.Output.WriteLine(
            $"Installed {manifest.Name}@{manifest.Version} to {destination.FullName}");

        // Said out loud because it is the thing a reader expects to have
        // happened and which deliberately did not.
        environment.Console.Output.WriteLine(
            $"The pin is unchanged. Run 'preflight pipeline use {manifest.Name}@{manifest.Version}' to switch to it.");

        if (!noGc)
        {
            Collect(environment, manifest.Name, keep);
        }

        return Task.FromResult(ExitCode.Success);
    }

    /// <summary>
    /// Which versions the sweep may remove.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pure, and separate from the sweep, because this is the part that is worth
    /// being sure about: it decides what gets deleted, and deleting is the one
    /// thing here that cannot be undone.
    /// </para>
    /// <para>
    /// Retention is counted <em>per pipeline</em> and never across the root. A
    /// game that publishes ten times a week would otherwise evict the pinned
    /// version of the game beside it. Everything referenced survives regardless
    /// of age — what is pinned, and what the workspace in front of us resolves
    /// to — and there is deliberately no registry of other checkouts on this
    /// disk: it would be state to maintain and invalidate, and a checkout
    /// somebody deleted would keep a version alive for ever.
    /// </para>
    /// </remarks>
    /// <param name="installed">Every installed version, ascending.</param>
    /// <param name="referenced">Versions that must survive whatever their age.</param>
    /// <param name="keep">How many of the newest to retain.</param>
    public static IReadOnlyList<PackageVersion> Collectable(
        IReadOnlyList<PackageVersion> installed,
        IReadOnlyCollection<PackageVersion> referenced,
        int keep)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(referenced);

        if (installed.Count <= keep)
        {
            return [];
        }

        return
        [
            .. installed
                .OrderByDescending(version => version)
                .Skip(keep)
                .Where(version => !referenced.Contains(version))
                .Order(),
        ];
    }

    private static void Collect(CommandEnvironment environment, string pipeline, int? keep)
    {
        var state = environment.MachineState;
        var installed = environment.InstalledPipelines.Versions(pipeline);

        var referenced = new List<PackageVersion>();

        if (state.Pins.TryGetValue(pipeline, out var pinned))
        {
            referenced.Add(pinned);
        }

        var collectable = Collectable(installed, referenced, keep ?? state.Keep);

        foreach (var version in collectable)
        {
            environment.InstallWriter.Remove(environment.InstallRoot.VersionDirectory(pipeline, version));

            // Printed, because a directory that quietly shrinks is the sort of
            // thing somebody finds a year later and cannot attribute.
            environment.Console.Output.WriteLine($"Removed {pipeline}@{version}");
        }
    }

    private static PackageManifest ReadManifest(IPackageArchive archive, string packagePath)
    {
        var entries = archive.Entries(packagePath);

        if (!entries.Any(entry => string.Equals(
            entry.RelativePath, PackageManifest.FileName, StringComparison.Ordinal)))
        {
            throw new PackageManifestException(
                $"{packagePath} carries no {PackageManifest.FileName}, so it is not a pipeline package.");
        }

        var temporary = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            File.WriteAllBytes(temporary, archive.Read(packagePath, PackageManifest.FileName));

            return InstalledPipelineReader.Read(temporary);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <remarks>
    /// The package's own version and the contract version are two axes and
    /// neither may decide for the other. This asks only the second question, and
    /// asks it through the same helper the loader uses, so the two cannot drift
    /// into disagreeing about which packages are loadable.
    /// </remarks>
    private static void RequireCompatibleContract(PackageManifest manifest, string packagePath)
    {
        if (!Version.TryParse(manifest.AbstractionsMinimumVersion, out var required))
        {
            throw new PackageManifestException(
                $"'{manifest.AbstractionsMinimumVersion}' in {packagePath} is not a contract version.");
        }

        if (!AbstractionsCompatibility.IsCompatible(required, AbstractionsCompatibility.HostVersion))
        {
            throw new PackageManifestException(
                $"{manifest.Name}@{manifest.Version} needs {AbstractionsCompatibility.AssemblyName} " +
                $"{required.ToString()}, and this build provides " +
                $"{AbstractionsCompatibility.HostVersion.ToString()}. " +
                "Refused here rather than at the next run, so one person sees it instead of every machine.");
        }
    }

    /// <remarks>
    /// Both directions. A file in the archive that the manifest does not list is
    /// refused, and not merely left unverified: a checksum map that covers only
    /// what it lists verifies nothing, because the unlisted assembly is exactly
    /// the one that ends up in <c>rules/</c> and is loaded on the next run. A
    /// file the manifest lists and the archive lacks is refused too — a package
    /// installing without its policy runs a smaller set of checks than it
    /// declares.
    /// </remarks>
    private static void RequireEveryFileAccountedFor(
        IPackageArchive archive, string packagePath, PackageManifest manifest)
    {
        var entries = archive.Entries(packagePath);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            // The manifest is the one file the map cannot cover: it holds the
            // digests, so a digest of itself would have to be computed after it
            // was written. What guards it is that everything it names must
            // match — a tampered manifest can only be a manifest for a package
            // whose files are also tampered, and those are checked here.
            if (string.Equals(entry.RelativePath, PackageManifest.FileName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!seen.Add(entry.RelativePath))
            {
                throw new PackageArchiveException(
                    $"{packagePath} holds '{entry.RelativePath}' twice, differing only in case.");
            }

            if (entry.RelativePath.Contains("..", StringComparison.Ordinal) ||
                Path.IsPathRooted(entry.RelativePath) ||
                entry.RelativePath.Contains(':', StringComparison.Ordinal))
            {
                throw new PackageArchiveException(
                    $"'{entry.RelativePath}' in {packagePath} escapes the package directory.");
            }

            if (!manifest.Sha256ByRelativePath.TryGetValue(entry.RelativePath, out var expected))
            {
                throw new PackageManifestException(
                    $"'{entry.RelativePath}' is in {packagePath} and not in its manifest. " +
                    "Every file a package carries has to be listed with its digest.");
            }

            var actual = Convert.ToHexString(
                SHA256.HashData(archive.Read(packagePath, entry.RelativePath)));

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new PackageManifestException(
                    $"'{entry.RelativePath}' in {packagePath} does not match the digest its manifest " +
                    "declares. The package is damaged or was edited after it was packed.");
            }
        }

        foreach (var listed in manifest.Sha256ByRelativePath.Keys)
        {
            if (!seen.Contains(listed))
            {
                throw new PackageManifestException(
                    $"'{listed}' is listed in the manifest of {packagePath} and is not in the package.");
            }
        }
    }
}
