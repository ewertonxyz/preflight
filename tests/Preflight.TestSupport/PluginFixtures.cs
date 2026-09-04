namespace Preflight.TestSupport;

using System.Runtime.Loader;
using static Preflight.TestSupport.RepositoryLayout;

/// <summary>
/// Real plugin assemblies on a real disk, for the parts of loading that a
/// substituted loader cannot answer.
/// </summary>
/// <remarks>
/// Three fixtures and no more. Everything else about plugin loading is a
/// judgement about a description of an assembly, and those are asserted through
/// <c>IAssemblyLoader</c> in <c>Preflight.Core.Tests</c> without a file
/// anywhere. It lives in the shared support project because the behaviour
/// specifications drive the real executable over the same directories. What is
/// left here are the questions whose answer <em>is</em> the runtime: which
/// contract a load context hands back, whether a context is collectible, and
/// what happens to a file that is not an assembly at all.
/// </remarks>
public static class PluginFixtures
{
    private const string SampleAssemblyFileName = "Sample.Production.Rules.dll";

    /// <summary>The id the sample rule declares.</summary>
    public const string SampleRuleId = "atlas.content.texture-dimension";

    /// <summary>
    /// A directory holding the built sample, as a production would deploy it.
    /// </summary>
    /// <param name="withContractCopy">
    /// Also drop a copy of <c>Preflight.Abstractions.dll</c> beside it — the
    /// mistake <c>Private="false"</c> exists to prevent, and the negative
    /// control without which the delegation assertions pass against no loader
    /// at all.
    /// </param>
    public static DirectoryInfo PluginDirectory(bool withContractCopy = false)
    {
        var directory = Directory.CreateTempSubdirectory("preflight-plugin-");
        var source = new DirectoryInfo(Path.GetDirectoryName(SampleAssemblyPath)!);

        foreach (var file in source.EnumerateFiles())
        {
            file.CopyTo(Path.Combine(directory.FullName, file.Name), overwrite: true);
        }

        if (withContractCopy)
        {
            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "Preflight.Abstractions.dll"),
                Path.Combine(directory.FullName, "Preflight.Abstractions.dll"),
                overwrite: true);
        }

        return directory;
    }

    /// <summary>
    /// Removes a fixture directory, and does not fail if it cannot.
    /// </summary>
    /// <remarks>
    /// An assembly loaded from a file keeps it open until its context is both
    /// unloaded and collected, and collection happens when the runtime decides
    /// rather than when a test finishes. A leftover temporary directory is
    /// litter; a test that failed over one would be reporting on the garbage
    /// collector's timing instead of on the loader.
    /// </remarks>
    public static void TryDelete(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        try
        {
            directory.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The sample assembly inside <paramref name="directory"/>.</summary>
    public static string SampleAssemblyIn(DirectoryInfo directory) =>
        Path.Combine(directory.FullName, SampleAssemblyFileName);

    /// <summary>
    /// A directory holding a file that ends in <c>.dll</c> and is not one.
    /// </summary>
    /// <remarks>
    /// Written here rather than committed. A deliberately broken binary in the
    /// repository is a file nobody can review and nobody can regenerate; four
    /// lines that produce one are neither.
    /// </remarks>
    public static DirectoryInfo BrokenPluginDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("preflight-broken-plugin-");

        File.WriteAllText(Path.Combine(directory.FullName, "Broken.Rules.dll"), "not an assembly");

        return directory;
    }

    /// <summary>
    /// A type that implements an <c>IValidationRule</c> which is not this
    /// tool's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fixture for one of the most irritating bugs in.NET plugin systems,
    /// produced from real assemblies rather than described. A plain load
    /// context with no <c>Load</c> override delegates everything to the default
    /// one — so a copy of the contract is loaded into it explicitly first, and
    /// the rules assembly loaded afterwards binds to that copy instead, because
    /// a context resolves against what it has already loaded before it falls
    /// back.
    /// </para>
    /// <para>
    /// What comes back is exactly what a broken plugin loader would hand the
    /// tool: a type whose <c>GetInterfaces</c> names
    /// <c>Preflight.Abstractions.Rules.IValidationRule</c> and which
    /// <c>IsAssignableFrom</c> rejects. It is built from
    /// <c>Preflight.Rules.dll</c> rather than from the sample so that this
    /// fixture depends on nothing the plugin tests also depend on.
    /// </para>
    /// </remarks>
    public static Type ForeignContractRuleType => ForeignRule.Value;

    private static readonly Lazy<Type> ForeignRule = new(() =>
    {
        // Not collectible, and kept alive by the Lazy. Unloading it would take
        // the type with it, and nothing here is large enough for that to matter.
        var isolated = new AssemblyLoadContext("preflight-foreign-contract");

        isolated.LoadFromAssemblyPath(
            Path.Combine(AppContext.BaseDirectory, "Preflight.Abstractions.dll"));

        return isolated
            .LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "Preflight.Rules.dll"))
            .GetType("Preflight.Rules.LargeFileRule", throwOnError: true)!;
    });

    private static string SampleAssemblyPath =>
        BuildOutputPathOf("samples/Sample.Production.Rules", SampleAssemblyFileName);
}
