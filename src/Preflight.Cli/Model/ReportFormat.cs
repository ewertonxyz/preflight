namespace Preflight.Cli.Model;

using Preflight.Abstractions.Model;
using Preflight.Core.Policy;

/// <summary>
/// The report formats.
/// </summary>
/// <remarks>
/// <c>Sarif</c> was planned before it was built, and refused by name during
/// parsing until it arrived. <c>report</c> reuses this enum and the parser
/// restricts it there to <c>console</c> and <c>json</c>: a second two-valued
/// enum would duplicate the concept in order to exclude a value the parser
/// already excludes.
/// </remarks>
public enum ReportFormat
{
    Console,
    Json,
    Sarif,
}
