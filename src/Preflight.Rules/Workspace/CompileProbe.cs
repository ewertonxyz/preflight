namespace Preflight.Rules;

using System.Text.Json.Serialization;
using Preflight.Abstractions.Services;

/// <summary>
/// The command that compiles without linking.
/// </summary>
/// <param name="Command">The executable to run.</param>
/// <param name="Arguments">
/// Its arguments. Any occurrence of <c>{probeOutput}</c> is replaced with a
/// path outside the workspace for the probe to write into.
/// </param>
/// <param name="WorkingDirectory">
/// Where to run it, relative to the workspace root. Defaults to the root.
/// </param>
/// <param name="Inputs">
/// Everything the probe reads, as paths relative to the workspace root. A
/// directory contributes every file under it. Absent means the probe is never
/// cached.
/// </param>
/// <remarks>
/// The <c>{probeOutput}</c> token is how the probe is kept from writing into
/// the workspace. The tool never does, and it matters twice over here: a
/// validation run must leave a checkout exactly as it found it, and the engine
/// runs rules at one level concurrently, so two probes writing to the same
/// intermediates would corrupt each other. But a compiler writes wherever it is
/// told, and told nothing it writes next to the sources. The read-only
/// <see cref="IFileSystem"/> cannot stop it, because the rule does not do the
/// writing — the child process does. The token is the one mechanism that can,
/// and the integration layer asserts the fixture is byte-identical after a
/// probe has run over it.
///
/// <para>
/// <c>Inputs</c> exists for the incremental cache, and it is the one part of
/// this manifest that can be wrong in a way the tool cannot detect. The engine
/// does not know what a compiler reads, and inferring it was rejected precisely
/// because an inferred set errs by optimism. So the workspace declares it — the
/// same arrangement as <c>minimumVersion</c> and <c>restoredMarker</c>, where
/// what the workspace needs is stated by the workspace.
/// </para>
/// <para>
/// The consequence has to be said plainly: a declaration that leaves out a
/// directory the compiler reads will serve a cached <c>Passed</c> after a
/// change in that directory. Omitting <c>Inputs</c> entirely is therefore the
/// default and the safe state — no declaration, no caching. A directory rather
/// than a glob for the same reason this manifest takes two version bounds
/// instead of a range syntax: every glob dialect is a parser to write and test
/// before a single file is compared.
/// </para>
/// </remarks>
public sealed record CompileProbe(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("arguments")] IReadOnlyList<string> Arguments,
    [property: JsonPropertyName("workingDirectory")] string? WorkingDirectory = null,
    [property: JsonPropertyName("inputs")] IReadOnlyList<string>? Inputs = null);
