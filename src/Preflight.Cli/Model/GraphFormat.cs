namespace Preflight.Cli.Model;

using Preflight.Abstractions.Model;
using Preflight.Core.Policy;

/// <summary>
/// The graph formats.
/// </summary>
/// <remarks>
/// Both arms were planned before either was implemented: the command declared
/// no <c>--format</c> at all, so <c>--format dot</c> was rejected as an unknown
/// option rather than with the message every other refusal gives. Both ship
/// now, with <c>text</c> the default and byte-identical to what the command
/// printed before.
/// </remarks>
public enum GraphFormat
{
    Text,
    Dot,
}
