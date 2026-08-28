namespace Preflight.Rules.Tests;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;

/// <summary>
/// Assembles a <see cref="RuleContext"/> with inert defaults.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RuleContext"/> has eight required members and most rule tests care
/// about one or two. Without this, every test would open with a dozen lines of
/// arrange in which the line that matters is invisible.
/// </para>
/// <para>
/// The services are substituted and left unconfigured on purpose. The unit
/// layer says the unit layer touches no disk and starts no process; a rule that
/// reached for one it was not given fails loudly here rather than passing
/// against the machine it happened to run on.
/// </para>
/// </remarks>
internal static class RuleFixture
{
    public static readonly DirectoryInfo WorkspaceRoot =
        new(Path.Combine(Path.GetTempPath(), "preflight-rule-tests"));

    public static RuleContext Context(
        IReadOnlyList<ChangedFile>? changedFiles = null,
        IPolicyReader? policy = null,
        IFileSystem? fileSystem = null,
        IProcessRunner? processes = null,
        ValidationStage stage = ValidationStage.PreSubmit,
        BuildTarget? target = null,
        DirectoryInfo? workspaceRoot = null) => new()
        {
            WorkspaceRoot = workspaceRoot ?? WorkspaceRoot,
            Stage = stage,
            Target = target ?? new BuildTarget("win64", "Development"),
            ChangedFiles = changedFiles ?? [],
            Policy = policy ?? EmptyPolicy(),
            Logger = Substitute.For<IRuleLogger>(),
            FileSystem = fileSystem ?? Substitute.For<IFileSystem>(),
            Processes = processes ?? Substitute.For<IProcessRunner>(),
        };

    /// <summary>
    /// A policy that answers every question with the caller's fallback.
    /// </summary>
    /// <remarks>
    /// Which is what an unconfigured rule genuinely sees: the schema leaves
    /// <c>settings</c> uninspected, so a key nobody wrote simply is not there.
    /// </remarks>
    public static IPolicyReader EmptyPolicy() => new StubPolicy(new Dictionary<string, object?>());

    public static IPolicyReader PolicyWith(string key, object? value) =>
        new StubPolicy(new Dictionary<string, object?> { [key] = value });

    public static ChangedFile Added(string path) => new(path, ChangeKind.Added);

    public static ChangedFile Modified(string path) => new(path, ChangeKind.Modified);

    public static ChangedFile Deleted(string path) => new(path, ChangeKind.Deleted);

    public static ChangedFile Renamed(string from, string to) => new(to, ChangeKind.Renamed, from);

    /// <remarks>
    /// Hand-written rather than substituted because <see cref="IPolicyReader"/>
    /// is generic in a way NSubstitute cannot express readably: every test
    /// would have to configure <c>GetValue&lt;long&gt;</c> and
    /// <c>GetValue&lt;string[]&gt;</c> separately, naming the type at each call
    /// site and getting a silent default when it named the wrong one.
    /// </remarks>
    private sealed class StubPolicy : IPolicyReader
    {
        private readonly IReadOnlyDictionary<string, object?> _values;

        public StubPolicy(IReadOnlyDictionary<string, object?> values)
        {
            _values = values;
        }

        public T GetValue<T>(string key, T fallback) =>
            TryGetValue<T>(key, out var value) ? value : fallback;

        public bool TryGetValue<T>(
            string key,
            [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T value)
        {
            if (_values.TryGetValue(key, out var stored) && stored is T typed)
            {
                value = typed;

                return true;
            }

            value = default;

            return false;
        }
    }
}
