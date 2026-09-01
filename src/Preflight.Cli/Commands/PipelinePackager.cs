namespace Preflight.Cli.Commands;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Preflight.Core;

/// <summary>
/// Raised when a source tree cannot be packed, or the output cannot be written.
/// </summary>
public sealed class PipelinePackException : ConfigurationLoadException
{
    public PipelinePackException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// <c>preflight pipeline pack</c>.
/// </summary>
/// <remarks>
/// <para>
/// The author's half of the package contract. The source tree carries a
/// <c>pipeline.json</c> stating what the package is — its name, its version,
/// which document is the policy and which assemblies are the rules — and this
/// command fills in the half nobody can write by hand: the digest of every file
/// it ships. The manifest that comes out is the one <c>pipeline install</c>
/// reads, and the two are the only ends of the channel this tool has. What
/// moves the file between them is the toolchain a studio already operates; see
/// ADR-033 for why the middle is deliberately absent rather than forgotten.
/// </para>
/// <para>
/// Every refusal here is exit 2, and each one exists because the alternative
/// produces a package that installs and is wrong: an output written inside the
/// tree becomes an input on the second run, a workspace manifest inside the
/// package ages silently while serving <c>(cached)</c> over a different
/// checkout, and an output written over an existing file destroys the archive
/// somebody may already have published a checksum for.
/// </para>
/// </remarks>
public static class PipelinePackager
{
    /// <summary>
    /// Packs a source tree into one deterministic archive.
    /// </summary>
    /// <param name="environment">Where the archive writer and the console are.</param>
    /// <param name="sourceDirectory">The tree to pack.</param>
    /// <param name="output">Where to write the archive. Must not exist.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <exception cref="PipelinePackException">The tree or the output is unusable.</exception>
    /// <exception cref="PackageManifestException">The tree's own manifest is unusable.</exception>
    public static Task<int> PackAsync(
        CommandEnvironment environment,
        string sourceDirectory,
        string output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(sourceDirectory);
        ArgumentNullException.ThrowIfNull(output);

        cancellationToken.ThrowIfCancellationRequested();

        var source = new DirectoryInfo(Path.GetFullPath(sourceDirectory));

        if (!source.Exists)
        {
            throw new PipelinePackException($"There is no directory at {source.FullName} to pack.");
        }

        var archivePath = Path.GetFullPath(output);

        RequireUsableOutput(source, archivePath);

        // Read before anything is enumerated, because it names the policy file
        // and the rule assemblies the tree is then checked against. An unusable
        // manifest is the manifest reader's refusal, word for word, so that a
        // package refused at pack time and one refused at install time complain
        // in the same sentence.
        var manifest = InstalledPipelineReader.Read(
            Path.Combine(source.FullName, PackageManifest.FileName));

        var files = Contents(source, cancellationToken);

        RequireNoWorkspaceManifest(source, files);
        RequireEverythingTheManifestNames(source, manifest, files);

        var digests = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var payload = new List<PackageFile>(files.Count + 1);

        foreach (var (relativePath, absolutePath) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = File.ReadAllBytes(absolutePath);

            digests[relativePath] = Convert.ToHexString(SHA256.HashData(content));
            payload.Add(new PackageFile(relativePath, content));
        }

        payload.Add(new PackageFile(
            PackageManifest.FileName, Encoding.UTF8.GetBytes(Serialize(manifest, digests))));

        environment.PackageArchive.Write(archivePath, payload);

        environment.Console.Output.WriteLine(
            $"Packed {manifest.Name}@{manifest.Version} to {archivePath}");
        environment.Console.Output.WriteLine(
            $"{digests.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"{(digests.Count == 1 ? "file" : "files")}, each listed with its digest.");

        return Task.FromResult(ExitCode.Success);
    }

    /// <remarks>
    /// Two refusals with one cause. An output that already exists may be an
    /// archive somebody has published a checksum for, and there is no
    /// <c>--force</c> because a flag that destroys a published artefact is a
    /// flag somebody types at the end of a long day. An output inside the tree
    /// is worse than untidy: the second run would pack the first run's archive,
    /// so the same tree would produce two different packages and the
    /// determinism this command exists for would fail underneath the test that
    /// asserts it.
    /// </remarks>
    private static void RequireUsableOutput(DirectoryInfo source, string archivePath)
    {
        if (File.Exists(archivePath) || Directory.Exists(archivePath))
        {
            throw new PipelinePackException(
                $"Something is already at {archivePath}. Move it aside, or pack somewhere else; " +
                "this command never replaces a package, and there is no --force.");
        }

        var root = Path.TrimEndingDirectorySeparator(source.FullName) + Path.DirectorySeparatorChar;

        if (archivePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new PipelinePackException(
                $"{archivePath} is inside {source.FullName}, which is the tree being packed. " +
                "The archive would become part of the next package. Write it somewhere else.");
        }
    }

    /// <remarks>
    /// Any depth, ignoring case, which is how <c>ReservedFileNames</c> already
    /// treats the same file name. <c>preflight.workspace.json</c> describes a
    /// checkout — its <c>compileProbe.inputs</c> change whenever <c>src/</c>
    /// does — and shipped inside a package it would age in silence while
    /// serving <c>(cached)</c> over a tree it had never seen.
    /// </remarks>
    private static void RequireNoWorkspaceManifest(
        DirectoryInfo source, List<(string Relative, string Absolute)> files)
    {
        var offender = files.FirstOrDefault(file => string.Equals(
            Path.GetFileName(file.Relative),
            WorkspaceManifestFileName,
            StringComparison.OrdinalIgnoreCase));

        if (offender.Relative is not null)
        {
            throw new PipelinePackException(
                $"'{offender.Relative}' is a workspace manifest, and {source.FullName} would ship it " +
                "inside the package. That file describes one checkout and goes stale silently in " +
                "anybody else's. Remove it from the tree being packed.");
        }
    }

    /// <remarks>
    /// The tree has to hold what its manifest claims, and it has to hold
    /// something. A package carrying a policy and no rules is the common case —
    /// most productions tighten limits without writing a line of C# — but a
    /// tree holding nothing except its own manifest is a package that installs
    /// and changes nothing, which is a way of shipping a mistake.
    /// </remarks>
    private static void RequireEverythingTheManifestNames(
        DirectoryInfo source,
        PackageManifest manifest,
        List<(string Relative, string Absolute)> files)
    {
        if (files.Count == 0)
        {
            throw new PipelinePackException(
                $"{source.FullName} holds nothing but its {PackageManifest.FileName}. " +
                "A package carries at least the policy document its manifest names.");
        }

        var present = new HashSet<string>(
            files.Select(file => file.Relative), StringComparer.OrdinalIgnoreCase);

        foreach (var named in new[] { manifest.PolicyFile }.Concat(manifest.RuleAssemblies))
        {
            if (!present.Contains(Normalise(named)))
            {
                throw new PipelinePackException(
                    $"The manifest names '{named}', and {source.FullName} does not hold it. " +
                    "A package that installs without a file it declares runs a smaller set of " +
                    "checks than its author published.");
            }
        }
    }

    /// <remarks>
    /// Ordinal ordering and forward slashes, decided here rather than left to
    /// the file system. Two checkouts of one repository enumerate in whatever
    /// order their directories happen to be laid out in, and a package whose
    /// bytes depend on that is a package whose published checksum is a
    /// coincidence.
    /// </remarks>
    private static List<(string Relative, string Absolute)> Contents(
        DirectoryInfo source, CancellationToken cancellationToken)
    {
        var files = new List<(string Relative, string Absolute)>();

        foreach (var path in Directory.EnumerateFiles(
            source.FullName, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Normalise(Path.GetRelativePath(source.FullName, path));

            // The manifest is written last, from this scan, so the copy on disk
            // is deliberately not carried through: it is missing the digests
            // that are the point of packing.
            if (string.Equals(relative, PackageManifest.FileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            files.Add((relative, path));
        }

        files.Sort((left, right) => string.CompareOrdinal(left.Relative, right.Relative));

        RequireDistinctIgnoringCase(source.FullName, [.. files.Select(file => file.Relative)]);

        return files;
    }

    /// <summary>
    /// Refuses a set of paths in which two differ only in case.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Public and taking its input as a list, for the reason
    /// <see cref="PipelineInstaller.Collectable"/> gives about the retention
    /// sweep: it decides a refusal, and a refusal is worth being sure about.
    /// Here there is a second reason that is not a preference. The condition
    /// cannot be reached through a real directory on Windows — NTFS refuses two
    /// names differing only in case, and refuses a junction that would produce
    /// them — so a test driving the packager end to end could never enter it.
    /// It is entirely reachable on the file systems that allow it, which is
    /// where the defect it prevents lives.
    /// </para>
    /// <para>
    /// The defect: the second file silently replaces the first on installation,
    /// so a package built on Linux installs one assembly fewer than its author
    /// packed, and every digest in the manifest still matches. Refused here
    /// rather than at install, because the author is the person who can rename
    /// a file and the machine installing it is not.
    /// </para>
    /// </remarks>
    /// <param name="source">The tree being packed, for the message.</param>
    /// <param name="relativePaths">Its contents, as relative paths.</param>
    /// <exception cref="PipelinePackException">Two paths differ only in case.</exception>
    public static void RequireDistinctIgnoringCase(
        string source, IReadOnlyList<string> relativePaths)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(relativePaths);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relative in relativePaths)
        {
            if (!seen.Add(relative))
            {
                throw new PipelinePackException(
                    $"{source} holds '{relative}' twice, differing only in case. " +
                    "One of the two would replace the other on installation.");
            }
        }
    }

    private static string Serialize(
        PackageManifest manifest, IReadOnlyDictionary<string, string> digests) =>
        JsonSerializer.Serialize(
            new
            {
                schemaVersion = PackageManifest.CurrentSchemaVersion,
                name = manifest.Name,
                version = manifest.Version.ToString(),
                policyFile = Normalise(manifest.PolicyFile),
                ruleAssemblies = manifest.RuleAssemblies.Select(Normalise).Order(StringComparer.Ordinal),
                abstractionsMinimumVersion = manifest.AbstractionsMinimumVersion,
                abstractionsMaximumVersion = manifest.AbstractionsMaximumVersion,
                sha256ByRelativePath = digests,
            },
            ManifestSerialization.Options);

    private static string Normalise(string relativePath) => relativePath.Replace('\\', '/');

    /// <remarks>
    /// Named here rather than taken from <c>PipelineSelector.ReservedFileNames</c>,
    /// which is a list of three and would say "one of these is not a pipeline
    /// overlay". This is one file with one reason, and the message has to name
    /// that reason.
    /// </remarks>
    private const string WorkspaceManifestFileName = "preflight.workspace.json";
}
