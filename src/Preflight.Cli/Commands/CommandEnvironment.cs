namespace Preflight.Cli.Commands;

using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Cli.Interactive;
using Preflight.Cli.Pipelines;
using Preflight.Cli.Reporting;
using Preflight.Cli.Services;
using Preflight.Cli.Storage;
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
/// of it and a test needs to replace any of it. Constructed once by
/// <see cref="PreflightCommandLine.RealEnvironment"/> from the real machine.
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
    /// Injected separately from <see cref="Processes"/>, which buffers both
    /// streams into strings and hands them back when the child exits. That is
    /// right for a rule reading a compiler's error list and wrong for a
    /// wrapper: a build that takes half an hour would print nothing until it
    /// finished, and a string is not a byte.
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
    /// output unchanged. A <see cref="TextWriter"/> cannot express that: it
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
    public required MachineEnvironment Machine { get; init; }

    /// <summary>
    /// The only thing here that writes inside the workspace.
    /// </summary>
    /// <remarks>
    /// A type of its own rather than a method on <see cref="FileSystem"/>,
    /// which declares reads and no writes and is handed to every rule that
    /// runs. Adding a write to it so that one command could scaffold a file
    /// would hand that capability to every rule at the same time, and a
    /// validation run that can edit the workspace it is judging is no longer
    /// judging it.
    /// </remarks>
    public required IWorkspaceFileWriter WorkspaceWriter { get; init; }

    /// <summary>Where installed pipeline packages live on this machine.</summary>
    /// <remarks>
    /// A machine fact, resolved once by
    /// <see cref="PreflightCommandLine.RealEnvironment"/> from the environment
    /// and never from the workspace: <c>PREFLIGHT_HOME</c> if it is set to
    /// something, otherwise the local application data directory. Neither
    /// available is a refusal naming both, never a path assembled around a
    /// null, and a root that contains or equals the workspace is exit 2 —
    /// packages installed inside the tree under validation would be scanned by
    /// the rules validating it.
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
    /// The third writer, and deliberately not the workspace one. Three coexist
    /// and none of them may be merged into another: the workspace writer
    /// refuses to replace a file, the machine state store always replaces, and
    /// this one swaps a whole version directory. Each has a test pinning the
    /// outcome the other two would produce, so a later harmonisation fails
    /// loudly instead of quietly adopting one rule for all three.
    /// </remarks>
    public required IInstallRootWriter InstallWriter { get; init; }

    /// <summary>
    /// Which pipeline this invocation uses, and what decided it.
    /// </summary>
    /// <remarks>
    /// Resolved once at the dispatch point and carried, because two consumers
    /// need the same answer: package resolution, which turns the name into an
    /// installed version, and policy resolution, which turns it into a file to
    /// read. Selecting twice means reading <c>preflight.base.json</c> twice and
    /// enumerating the workspace root twice, and the two reads can disagree if
    /// anything edits the file in between — the same argument the install root
    /// is resolved once for.
    /// </remarks>
    public PipelineSelection Selection { get; init; } = PipelineSelection.None;

    /// <summary>
    /// The checkout's base document, parsed once.
    /// </summary>
    /// <remarks>
    /// Carried beside <see cref="Selection"/> because it is what the selection
    /// was derived from, and because package resolution needs a second answer
    /// out of the same file — the version range the checkout accepts. Opening
    /// it twice is two answers to one question.
    /// </remarks>
    public CheckoutDocument Checkout { get; init; } = CheckoutDocument.Absent;

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
