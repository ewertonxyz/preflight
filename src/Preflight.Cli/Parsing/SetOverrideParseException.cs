namespace Preflight.Cli.Parsing;

using System.Globalization;
using Preflight.Abstractions.Rules;
using Preflight.Core;
using Preflight.Core.Policy;

/// <summary>
/// A <c>--set</c> argument that could not be parsed.
/// </summary>
/// <remarks>
/// Derives from <see cref="ConfigurationLoadException"/> so it lands on exit 2
/// through the same <c>catch</c> as an invalid policy file and an invalid rule
/// graph. That is not a convenience: every configuration problem at load time,
/// and a malformed flag is a configuration problem the user can fix — not an
/// internal error, which is what exit 3 would claim.
/// </remarks>
public sealed class SetOverrideParseException : ConfigurationLoadException
{
    public SetOverrideParseException(string message)
        : base(message)
    {
    }
}
