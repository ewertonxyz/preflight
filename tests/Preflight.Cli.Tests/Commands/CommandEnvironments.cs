namespace Preflight.Cli.Tests.Commands;

using System.Text;
using NSubstitute;
using Preflight.Abstractions.Rules;
using Preflight.Cli.Commands;
using Preflight.Cli.Interactive;
using Preflight.Core;
using Preflight.Core.Caching;
using Preflight.Core.History;
using Preflight.Core.Plugins;
using Preflight.Core.Policy;

/// <summary>
/// Builds a <see cref="CommandEnvironment"/> with the real machine replaced
/// wherever a test needs to see what happened.
/// </summary>
/// <remarks>
/// One factory for every command test. <c>CommandEnvironment</c> became a record
/// of named required properties in the history so that adding a member is additive;
/// this is the other half of that promise — a new member gets its test default
/// here, once, instead of in every test class that constructs one.
/// </remarks>
public static class CommandEnvironments
{
    /// <summary>
    /// A directory holding nothing, shared by every environment that does not
    /// care where the executable is.
    /// </summary>
    /// <remarks>
    /// Created once and never written to, so the tests that share it cannot
    /// interfere with one another. xUnit v3 runs classes in parallel inside an
    /// assembly, and a per-test temporary directory would be the more obvious
    /// choice right up to the point where one of them decided to put a plugin
    /// in it.
    /// </remarks>
    private static readonly Lazy<DirectoryInfo> EmptyDirectory =
        new(() => Directory.CreateTempSubdirectory("preflight-no-plugins-"));

    /// <summary>
    /// An install root holding nothing, for every test that does not install.
    /// </summary>
    /// <remarks>
    /// Shared and never written to, on the same terms as
    /// <see cref="EmptyDirectory"/>. A test that installs passes its own root
    /// instead, because two classes running in parallel against one root would
    /// see each other's packages.
    /// </remarks>
    private static readonly Lazy<DirectoryInfo> EmptyInstallRoot =
        new(() => Directory.CreateTempSubdirectory("preflight-install-root-"));

    /// <summary>The machine the history file names are asserted against.</summary>
    public static EngineEnvironment Machine { get; } = new()
    {
        ProcessorCount = 8,
        MachineName = "WKS-1234",
        ProcessId = 4242,
    };

    public static CommandEnvironment For(
        DirectoryInfo workspace,
        TextWriter output,
        TextWriter error,
        TimeProvider clock,
        IReadOnlyList<IValidationRule>? rules = null,
        IEnvironmentReader? reader = null,
        IHistoryStore? history = null,
        IRuleCacheStore? cache = null,
        IChildProcessLauncher? children = null,
        Stream? rawOutput = null,
        Stream? rawError = null,
        DirectoryInfo? executableDirectory = null,
        Func<IAssemblyLoader>? assemblyLoader = null,
        IWorkspaceFileWriter? workspaceWriter = null,
        PipelineInstallRoot? installRoot = null,
        IInstalledPipelineReader? installedPipelines = null,
        IMachineStateStore? machineStateStore = null,
        MachineState? machineState = null,
        IPackageArchive? packageArchive = null,
        IInstallRootWriter? installWriter = null,
        IPipelinePicker? picker = null,
        bool isInputInteractive = false)
    {
        // A root of its own, and never the machine's. Without this every command
        // test would resolve against the real %LOCALAPPDATA%\Preflight, and a
        // developer who had installed a pipeline would get different results
        // from one who had not — the dependency on the machine that the seam
        // exists to remove, reintroduced through its default.
        var root = installRoot ?? new PipelineInstallRoot(EmptyInstallRoot.Value);
        var store = machineStateStore ?? new MachineStateStore();

        return new()
        {
            WorkspaceRoot = workspace,
            InstallRoot = root,
            InstalledPipelines = installedPipelines ?? new InstalledPipelineReader(root),
            MachineStateStore = store,

            // Read from the store, exactly as the real environment does, rather
            // than defaulted to empty. An absent file still reads as empty, so
            // nothing changes for a test that never writes one — but a test that
            // invokes 'pipeline use' and then a run now sees the pin it just
            // wrote, instead of quietly running against a machine that forgot.
            MachineState = machineState ?? store.Read(root.MachineStatePath),
            PackageArchive = packageArchive ?? new PackageArchive(),
            Picker = picker ?? new RefusingPicker(),
            InstallWriter = installWriter ?? new InstallRootWriter(),
            FileSystem = new PhysicalFileSystem(),
            Processes = new ProcessRunner(),
            Children = children ?? new ChildProcessLauncher(),
            Environment = reader ?? NoCi(),
            Console = new ConsoleCapabilities(
            output,
            Encoding.UTF8,
            IsInteractive: false,

            // Redirected, always, unless a test says otherwise. Every picker
            // asks this before it draws, so the default is the one state in
            // which no test can block waiting for a keyboard that is not there.
            isInputInteractive,
            ConsoleCapabilities.DefaultWidth),
            Error = error,
            RawOutput = rawOutput ?? Stream.Null,
            RawError = rawError ?? Stream.Null,
            WorkspaceWriter = workspaceWriter ?? new WorkspaceFileWriter(),
            Rules = rules ?? Preflight.Rules.Tests.BuiltInRuleDescriptorsTests.Discovered(),

            // An empty directory, never AppContext.BaseDirectory. The implicit
            // rules/ of plugin loading is resolved against this, and a developer
            // who ever drops a plugin beside their test binary would otherwise
            // change the outcome of every command test on their machine only.
            ExecutableDirectory = executableDirectory ?? EmptyDirectory.Value,
            AssemblyLoader = assemblyLoader ?? (() => new PluginAssemblyLoader()),
            TimeProvider = clock,
            History = history ?? new FileHistoryStore(),
            Cache = cache ?? new FileRuleCacheStore(),
            Machine = Machine,
        };
    }

    /// <remarks>
    /// A build agent exports <c>CI</c>, and the local-overlay rule's overlay table turns on
    /// exactly that. Without this, a test would apply the local overlay or not
    /// according to where it ran.
    /// </remarks>
    public static IEnvironmentReader NoCi()
    {
        var reader = Substitute.For<IEnvironmentReader>();

        reader.GetVariable(Arg.Any<string>()).Returns((string?)null);

        return reader;
    }
}

/// <summary>
/// The picker a test gets when it did not ask for one.
/// </summary>
/// <remarks>
/// It throws rather than returning a plausible answer. The real
/// <c>SpectrePipelinePicker</c> would try to read a terminal that is not there,
/// and a substitute returning the first choice would let a test pass through an
/// interactive path it never meant to exercise — which is the same false green
/// the gate in front of it exists to prevent.
/// </remarks>
public sealed class RefusingPicker : IPipelinePicker
{
    public string Pick(SelectionModel model) =>
        throw new InvalidOperationException(
            "This test reached the picker. Pass one in, or pass isInputInteractive: false.");
}
