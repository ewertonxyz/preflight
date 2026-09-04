namespace Preflight.Core;

/// <summary>
/// Base for every way a run can be rejected before any rule executes.
/// </summary>
/// <remarks>
/// <para>
/// An invalid policy and an invalid rule graph go to the same place: exit 2, at
/// load time, never during execution. The two carry different evidence — a
/// policy error knows a file and a line, a dependency cycle declared in
/// compiled descriptors has neither — so they stay separate types rather than
/// one type whose file and line are always null for half its uses.
/// </para>
/// <para>
/// What they do share is the caller. This base is what lets the code that
/// chooses the process exit code have one <c>catch</c> instead of one per
/// validation stage, which is also what stops a third kind of configuration
/// error, added later, from being silently missed by a caller that only knew
/// about two.
/// </para>
/// </remarks>
public abstract class ConfigurationLoadException : Exception
{
    protected ConfigurationLoadException(string message)
        : base(message)
    {
    }
}
