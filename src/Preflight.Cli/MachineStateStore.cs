namespace Preflight.Cli;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Preflight.Core.Policy;

/// <summary>
/// Raised when the machine state file exists and cannot be understood.
/// </summary>
/// <remarks>
/// A configuration error, so it exits 2 through the one mapping that decides
/// exit codes. The machine's own state being unreadable is a condition of the
/// machine, not a defect in this tool.
/// </remarks>
public sealed class MachineStateException : Preflight.Core.ConfigurationLoadException
{
    public MachineStateException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The real machine state store: one small JSON file beside the installed
/// packages.
/// </summary>
/// <remarks>
/// <para>
/// Staging file then move, as the workspace writer and the cache store both do,
/// with one difference that matters here and is easy to get wrong: the staging
/// file is created <em>inside the install root</em> rather than in the system
/// temporary directory. <see cref="File.Move(string, string, bool)"/> is not
/// atomic across volumes, and <c>PREFLIGHT_HOME</c> pointing at another drive is
/// an ordinary thing for somebody to do.
/// </para>
/// <para>
/// The move replaces, and that is the opposite of what
/// <c>IWorkspaceFileWriter</c> promises. Both are correct: one guards a file a
/// person authored, the other holds a pin whose whole purpose is to be changed.
/// A test on each pins the opposite outcome, so that a later refactor which
/// "unifies" the two breaks loudly rather than quietly granting overwrite to the
/// workspace.
/// </para>
/// </remarks>
public sealed class MachineStateStore : IMachineStateStore
{
    /// <remarks>
    /// Camel case both ways, and case-insensitive on the way in. The file is
    /// small, is written by this tool and is read by people, so it is spelled
    /// the way the rest of the tool's machine-readable output is.
    /// </remarks>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc />
    public MachineState Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            return MachineState.Empty;
        }

        MachineStateDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<MachineStateDocument>(File.ReadAllText(path), Options);
        }
        catch (Exception exception) when (exception is JsonException or IOException
            or UnauthorizedAccessException)
        {
            // Named rather than reset. A reset drops every pin, and the next run
            // resolves to the newest installed version instead of the pinned one
            // with nothing printed about it — the wrong-package run, reached by
            // being helpful.
            throw new MachineStateException(
                $"Could not read the machine state at {path}: {exception.Message}. " +
                "Fix the file, or move it aside; it will be recreated.");
        }

        if (document is null)
        {
            throw new MachineStateException(
                $"The machine state at {path} is empty. Move it aside; it will be recreated.");
        }

        return new MachineState { Pins = ReadPins(document, path), Keep = ReadKeep(document, path) };
    }

    /// <inheritdoc />
    public void Write(string path, MachineState state)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(state);

        var directory = Path.GetDirectoryName(path)!;

        Directory.CreateDirectory(directory);

        var staging = Path.Combine(directory, Path.GetRandomFileName());

        var document = new MachineStateDocument
        {
            Keep = state.Keep,
            Pins = state.Pins.ToDictionary(
                pair => pair.Key, pair => pair.Value.ToString(), StringComparer.OrdinalIgnoreCase),
        };

        File.WriteAllText(staging, JsonSerializer.Serialize(document, Options), Encoding.UTF8);

        try
        {
            File.Move(staging, path, overwrite: true);
        }
        catch
        {
            File.Delete(staging);

            throw;
        }
    }

    private static Dictionary<string, PackageVersion> ReadPins(
        MachineStateDocument document, string path)
    {
        var pins = new Dictionary<string, PackageVersion>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, text) in document.Pins ?? [])
        {
            if (!PipelineName.IsValid(name))
            {
                throw new MachineStateException(
                    $"'{name}' in {path} is not a pipeline name.");
            }

            if (!PackageVersion.TryParse(text, out var version))
            {
                throw new MachineStateException(
                    $"'{text}' pinned for '{name}' in {path} is not a package version. " +
                    "Expected three numbers, as in '1.4.0'.");
            }

            // Two names that differ only in case would both address the same
            // directory on this file system, and the dictionary would take
            // whichever the reader reached last. Refused for the reason
            // ADR-030 nº8 refuses two target keys of the same specificity.
            if (!pins.TryAdd(name, version!))
            {
                throw new MachineStateException(
                    $"'{name}' is pinned twice in {path}, differing only in case. Keep one.");
            }
        }

        return pins;
    }

    private static int ReadKeep(MachineStateDocument document, string path)
    {
        if (document.Keep is not { } keep)
        {
            return MachineState.DefaultKeep;
        }

        return keep >= 0
            ? keep
            : throw new MachineStateException($"'keep' in {path} must not be negative.");
    }

    private sealed record MachineStateDocument
    {
        public int? Keep { get; init; }

        public Dictionary<string, string>? Pins { get; init; }
    }
}
