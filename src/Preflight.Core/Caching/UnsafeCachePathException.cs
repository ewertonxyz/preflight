namespace Preflight.Core.Caching;

/// <summary>
/// <c>cachePath</c> points somewhere <c>preflight cache clear</c> will not
/// empty.
/// </summary>
/// <remarks>
/// A <see cref="ConfigurationLoadException"/>, so it reaches exit 2 through the
/// one catch the exit-code mapping already has. It is a configuration error in
/// the most literal sense: the policy names a directory, and the tool refuses
/// the instruction rather than carrying it out.
/// </remarks>
public sealed class UnsafeCachePathException : ConfigurationLoadException
{
    public UnsafeCachePathException(string message)
        : base(message)
    {
    }
}
