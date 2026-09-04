namespace Preflight.Core.Changes;

/// <summary>
/// The change source could not produce a list.
/// </summary>
/// <remarks>
/// A <see cref="ConfigurationLoadException"/>, so it reaches exit 2 rather than
/// exit 3. Every way this fails — a ref that does not resolve, a directory that
/// is not a repository, git missing from PATH — is something the person running
/// the tool can fix, and exit 3 is reserved for defects in the tool itself.
/// </remarks>
public sealed class ChangeSourceException : ConfigurationLoadException
{
    public ChangeSourceException(string message)
        : base(message)
    {
    }
}
