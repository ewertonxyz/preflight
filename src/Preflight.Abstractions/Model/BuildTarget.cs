namespace Preflight.Abstractions.Model;

/// <summary>
/// The platform and configuration a run is validating for.
/// </summary>
public sealed record BuildTarget(string Platform, string Configuration);
