namespace Preflight.Cli.Pipelines;

using Preflight.Core.Policy;

/// <summary>
/// The range of package versions a checkout accepts, from <c>requiresPipeline</c>.
/// </summary>
/// <remarks>
/// <para>
/// It carries no name. The pipeline is already named by the <c>pipeline</c> key
/// in the same file, and a second place to write the same name is a second
/// place for the two to disagree with nothing deciding which wins. A
/// <c>requiresPipeline</c> with no <c>pipeline</c> beside it bounds a name
/// nobody declared, and is refused at load.
/// </para>
/// <para>
/// Minimum inclusive and mandatory; maximum exclusive and optional. An absent
/// minimum would say "any version ever published", which is not a bound. An
/// absent maximum is a real choice for a checkout that does not yet know where
/// the next major hurts, and <c>pipeline declare</c> writes both so that the
/// default is the careful one.
/// </para>
/// <para>
/// Two keys rather than a range expression such as <c>&gt;=1.3 &lt;2.0</c>: that
/// is a parser to write and test in order to say what two keys already say,
/// and the workspace manifest has spelled every version range as two keys since
/// it existed.
/// </para>
/// </remarks>
/// <param name="Minimum">The lowest accepted version, inclusive.</param>
/// <param name="Maximum">The first rejected version, or <see langword="null"/>.</param>
public sealed record PipelineRequirement(PackageVersion Minimum, PackageVersion? Maximum)
{
    /// <summary>The root key this is read from.</summary>
    public const string KeyName = "requiresPipeline";

    /// <summary>The member holding the inclusive lower bound.</summary>
    public const string MinimumMember = "minimumVersion";

    /// <summary>The member holding the exclusive upper bound.</summary>
    public const string MaximumMember = "maximumVersion";

    /// <summary>
    /// Reads the requirement out of a parsed policy document, or refuses it.
    /// </summary>
    /// <remarks>
    /// Every refusal here is a configuration error at load, aggregated with the
    /// rest, never a requirement that silently turns out to be absent — which is
    /// the shape a run would take if a malformed value were skipped: the
    /// checkout would stop bounding anything and nobody would be told.
    /// </remarks>
    /// <param name="document">The parsed document.</param>
    /// <param name="fileName">The file, for the message.</param>
    /// <param name="filePath">The full path, for the error's own field.</param>
    /// <param name="pipelineDeclared">
    /// Whether the same document names a pipeline, under either spelling.
    /// </param>
    /// <returns>The requirement, or <see langword="null"/> when the key is absent.</returns>
    /// <exception cref="PolicyValidationException">The key is present and unusable.</exception>
    public static PipelineRequirement? Read(
        PolicyDocument document, string fileName, string? filePath, bool pipelineDeclared)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!document.Root.TryGetPath([KeyName], out var node) || node is null)
        {
            return null;
        }

        if (node is not PolicyNode.ObjectNode)
        {
            throw Refusal($"'{KeyName}' in {fileName} must be an object.", filePath);
        }

        // Refused here rather than left to policy validation, which never sees
        // preflight.base.json unless the selected pipeline extends it — so a
        // requirement bounding a name nobody declared would otherwise pass
        // unread and bound nothing.
        if (!pipelineDeclared)
        {
            throw Refusal(
                $"'{KeyName}' in {fileName} needs a 'pipeline' beside it: " +
                "a version range has to say which pipeline it bounds.",
                filePath);
        }

        var minimum = RequiredVersion(document, MinimumMember, fileName, filePath);
        var maximum = OptionalVersion(document, MaximumMember, fileName, filePath);

        if (maximum is not null && maximum <= minimum)
        {
            throw Refusal(
                $"'{KeyName}' in {fileName} has a '{MinimumMember}' of {minimum} that is not " +
                $"below its '{MaximumMember}' of {maximum}. The maximum is exclusive.",
                filePath);
        }

        return new PipelineRequirement(minimum, maximum);
    }

    private static PackageVersion RequiredVersion(
        PolicyDocument document, string member, string fileName, string? filePath) =>
        OptionalVersion(document, member, fileName, filePath)
            ?? throw Refusal(
                $"'{KeyName}' in {fileName} needs '{member}'. " +
                "A range that is open below does not bound anything.",
                filePath);

    private static PackageVersion? OptionalVersion(
        PolicyDocument document, string member, string fileName, string? filePath)
    {
        if (!document.TryGetRaw($"{KeyName}.{member}", out var raw) || raw is null)
        {
            return null;
        }

        if (raw is not string text)
        {
            throw Refusal($"'{KeyName}.{member}' in {fileName} must be a string.", filePath);
        }

        if (!PackageVersion.TryParse(text, out var version))
        {
            throw Refusal(
                $"'{text}' in {fileName} is not a package version. " +
                "Expected three numbers, as in '1.4.0'.",
                filePath);
        }

        return version;
    }

    private static PolicyValidationException Refusal(string message, string? filePath) =>
        new([new PolicyValidationError(message, filePath, null, KeyName)]);
}
