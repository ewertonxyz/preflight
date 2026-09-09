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
/// In the CLI on purpose, and it stays here even though the contract now also
/// carries an environment probe a rule receives. The two answer different
/// questions: this one tells the host whether it is running on a build machine
/// and where the tool is installed, and the other is a capability handed to a
/// rule that checks the variables a workspace declares. Merging them would put
/// continuous-integration detection inside the versioned contract every plugin
/// compiles against, and would let a rule ask a question that is the host's to
/// answer.
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
