namespace Preflight.Cli.Commands;

using Preflight.Abstractions;
using Preflight.Cli.Interactive;
using Preflight.Core.Caching;
using Preflight.Core.History;
using Preflight.Core.Plugins;
using Preflight.Core.Policy;

/// <summary>
/// Everything the commands need from the outside world.
/// </summary>
/// <remarks>
/// <para>
/// One record rather than a dozen parameters, because every command needs most
/// of it and a test needs to replace any of it. Constructed once in
/// <c>Program</c> from the real machine.
/// </para>
/// <para>
/// Properties rather than a positional record, and the change was made when four
/// more members arrived at once. A positional record turns every addition into a
/// compile break at every construction site, which is a cost paid again by every
/// change; named required properties make an addition additive
/// everywhere except where the new member is actually needed.
/// </para>
/// </remarks>
public sealed record CommandEnvironment
{
    /// <summary>The directory being validated.</summary>
    public required DirectoryInfo WorkspaceRoot { get; init; }

    /// <summary>Read-only access to it.</summary>
    public required IFileSystem FileSystem { get; init; }

    /// <summary>How a rule starts a child process and reads what it printed.</summary>
    public required IProcessRunner Processes { get; init; }

    /// <summary>
    /// How <c>measure</c> starts a child process and gets out of its way.
    /// </summary>
    /// <remarks>
    /// A separate seam from <see cref="Processes"/>: one buffers and returns,
    /// the other streams and propagates, and they are not the same contract
    /// wearing two names.
    /// </remarks>
    public required IChildProcessLauncher Children { get; init; }

    /// <summary>Where CI variables are read from.</summary>
    public required IEnvironmentReader Environment { get; init; }

    /// <summary>What the console can render.</summary>
    public required ConsoleCapabilities Console { get; init; }

    /// <summary>Where diagnostics and rule logs go.</summary>
    public required TextWriter Error { get; init; }

    /// <summary>
    /// The process's own standard output, as bytes.
    /// </summary>
    /// <remarks>
    /// Only <c>measure</c> uses it, and only because it propagates the child's
    /// output is propagated. A <see cref="TextWriter"/> cannot express that: it
    /// decodes and re-encodes, which changes the bytes of the command being
    /// measured.
    /// </remarks>
    public required Stream RawOutput { get; init; }

    /// <summary>The process's own standard error, as bytes.</summary>
    public required Stream RawError { get; init; }

    /// <summary>The built-in rules, before any plugin is loaded.</summary>
    /// <remarks>
    /// What a command actually executes is this set combined with whatever the
    /// plugin paths contributed, which <c>PreflightCommandLine.Run</c> resolves
    /// once for every command. Keeping the built-ins here rather than the
    /// combined set is what lets a collision between a plugin and a built-in be
    /// reported with both assembly names — the two are the same kind of
    /// citizen, and a set that had already merged them could not tell them
    /// apart.
    /// </remarks>
    public required IReadOnlyList<IValidationRule> Rules { get; init; }

    /// <summary>
    /// Where the executable lives, and therefore where the implicit
    /// <c>rules/</c> directory is looked for.
    /// </summary>
    /// <remarks>
    /// A member rather than a read of <c>AppContext.BaseDirectory</c> at the
    /// point of use, for two reasons that both bite. A test needs to point it
    /// at an empty directory, or the machine's own <c>rules/</c> would decide
    /// whether the test passes. And the workspace root is emphatically not the
    /// answer: a workspace is frequently a checkout whose contents the person
    /// running <c>preflight</c> did not write, and resolving plugins against it
    /// would execute code committed to the repository under validation.
    /// </remarks>
    public required DirectoryInfo ExecutableDirectory { get; init; }

    /// <summary>
    /// Opens a plugin assembly. A factory, because the loader owns every load
    /// context it creates and the caller releases them when the command ends.
    /// </summary>
    public required Func<IAssemblyLoader> AssemblyLoader { get; init; }

    /// <summary>
    /// The clock. Injected so the byte-identical guarantee is testable at all,
    /// and so the month in a history file name is not the month the test
    /// happened to run in.
    /// </summary>
    public required TimeProvider TimeProvider { get; init; }

    /// <summary>Where a history line is appended.</summary>
    public required IHistoryStore History { get; init; }

    /// <summary>Where cached rule results live.</summary>
    public required IRuleCacheStore Cache { get; init; }

    /// <summary>The machine facts that name a history file.</summary>
    public required EngineEnvironment Machine { get; init; }

    /// <summary>
    /// The one seam that writes inside the workspace.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="FileSystem"/>, which is read-only by
    /// construction and is handed to every rule. See ADR-028.
    /// </remarks>
    public required IWorkspaceFileWriter WorkspaceWriter { get; init; }

    /// <summary>Where installed pipeline packages live on this machine.</summary>
    /// <remarks>
    /// A machine fact, resolved once in <c>Program</c> from the environment, and
    /// never from the workspace. See ADR-032.
    /// </remarks>
    public required PipelineInstallRoot InstallRoot { get; init; }

    /// <summary>What is installed.</summary>
    public required IInstalledPipelineReader InstalledPipelines { get; init; }

    /// <summary>The pins and the retention setting.</summary>
    public required IMachineStateStore MachineStateStore { get; init; }

    /// <summary>The state as it was when this command started.</summary>
    /// <remarks>
    /// Read once and carried, rather than re-read at each point of use. A
    /// command that consulted the pins twice could see two different answers
    /// while another process installed, and the second half of its own work
    /// would then disagree with the first.
    /// </remarks>
    public required MachineState MachineState { get; init; }

    /// <summary>Reads a package archive.</summary>
    public required IPackageArchive PackageArchive { get; init; }

    /// <summary>
    /// How a command asks the person at the keyboard.
    /// </summary>
    /// <remarks>
    /// Reached only through <c>PipelinePicker.Choose</c>, which decides whether
    /// there is anybody to ask before this is touched. <c>init</c> with a real
    /// default rather than <c>required</c>, because the gate in front of it is
    /// what keeps a test safe: the test environment factory reports a redirected
    /// stdin, so <c>Choose</c> refuses before reaching this and no test can
    /// accidentally block on a terminal that is not there.
    /// </remarks>
    public IPipelinePicker Picker { get; init; } = new SpectrePipelinePicker();

    /// <summary>
    /// Writes inside the install root.
    /// </summary>
    /// <remarks>
    /// The third write seam, and deliberately not the workspace one: that
    /// refuses to replace a file, and this replaces a whole version directory.
    /// See ADR-033.
    /// </remarks>
    public required IInstallRootWriter InstallWriter { get; init; }

    /// <summary>
    /// The package this invocation resolved to, or <see langword="null"/> when
    /// none took part.
    /// </summary>
    /// <remarks>
    /// Resolved once at the dispatch point and carried, because two consumers
    /// need the same answer: plugin composition, which has to see the package's
    /// rules, and policy resolution, which reads the package's policy. Resolving
    /// it twice could produce two answers while another process installed.
    /// <c>init</c> and not <c>required</c>, so a test that does not care about
    /// packages constructs an environment without naming it.
    /// </remarks>
    public InstalledPipeline? ResolvedPackage { get; init; }
}
