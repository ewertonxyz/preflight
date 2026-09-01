namespace Preflight.Cli.Services;

/// <summary>
/// Reads environment variables.
/// </summary>
/// <remarks>
/// <para>
/// An injection point over one static call, and it earns its place. CI
/// detection reads five
/// variables, and the interesting cases are "present but empty" and "two
/// present at once" — which a test can only set up by mutating process-wide
/// state that has no teardown and is visible to every other test class running
/// concurrently, since xUnit v3 parallelises classes within an assembly.
/// </para>
/// <para>
/// Internal to the CLI on purpose. Environment inspection belongs to the host,
/// and <c>Preflight.Abstractions</c> is a versioned contract that should not
/// grow a member because a test needed one.
/// </para>
/// </remarks>
public interface IEnvironmentReader
{
    /// <summary>
    /// The value of <paramref name="name"/>, or <see langword="null"/> if it is
    /// not set.
    /// </summary>
    string? GetVariable(string name);
}
