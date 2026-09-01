namespace Preflight.Cli.Pipelines;

/// <summary>
/// What a pipeline package says about itself.
/// </summary>
/// <remarks>
/// <para>
/// Lives in the package as <c>pipeline.json</c>, and is the contract between
/// <c>pipeline pack</c> and <c>pipeline install</c>. Everything an install has
/// to decide before writing a byte is in here: what the package is called, which
/// version it is, which policy document it carries, which assemblies it brings,
/// which range of <c>Preflight.Abstractions</c> those assemblies need, and the
/// digest of every file it claims to contain.
/// </para>
/// <para>
/// The two version ranges in this record are the false friend of the phase and
/// they never interact. <see cref="Version"/> says which delivery of the policy
/// and the rules this is; <see cref="AbstractionsMinimumVersion"/> says whether
/// those assemblies load in this binary at all. A patch difference decides
/// nothing for the second and everything for the first.
/// </para>
/// </remarks>
/// <param name="SchemaVersion">The manifest schema. An unknown one is refused, never read best-effort.</param>
/// <param name="Name">The pipeline name.</param>
/// <param name="Version">The package version.</param>
/// <param name="PolicyFile">The policy document, relative to the package root.</param>
/// <param name="RuleAssemblies">The rule assemblies, relative to the package root.</param>
/// <param name="AbstractionsMinimumVersion">Lowest contract version these assemblies load against, inclusive.</param>
/// <param name="AbstractionsMaximumVersion">First contract version they do not, exclusive, or null.</param>
/// <param name="Sha256ByRelativePath">
/// Every file the package contains, and its digest. A file in the archive that
/// is absent from this map is refused rather than installed: a checksum map that
/// covers only what it lists verifies nothing, because the unlisted assembly is
/// the one that ends up in <c>rules/</c> and is loaded on the next run.
/// </param>
public sealed record PackageManifest(
    int SchemaVersion,
    string Name,
    PackageVersion Version,
    string PolicyFile,
    IReadOnlyList<string> RuleAssemblies,
    string AbstractionsMinimumVersion,
    string? AbstractionsMaximumVersion,
    IReadOnlyDictionary<string, string> Sha256ByRelativePath)
{
    /// <summary>The only schema this binary understands.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The manifest's file name inside a package.</summary>
    public const string FileName = "pipeline.json";
}
