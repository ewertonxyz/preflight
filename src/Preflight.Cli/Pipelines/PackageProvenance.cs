namespace Preflight.Cli.Pipelines;

using Preflight.Core.Policy;

/// <summary>
/// Marks every value that came out of an installed package with the package it
/// came from.
/// </summary>
/// <remarks>
/// <para>
/// Applied to the parsed document before anything merges it, so the wrapper sits
/// outside whatever the file itself produced — a value inside a <c>targets</c>
/// block ends up as a package wrapping a target wrapping a file and a line, and
/// <c>explain</c> prints all three.
/// </para>
/// <para>
/// Without this, a policy path in a report points at a file that does not exist
/// in the checkout: the reader goes looking for <c>acme.json</c> in the
/// workspace and finds nothing. Worse, two runs of one commit against two
/// different packages produce byte-identical output, so nothing in the report
/// says which set of limits the run was actually judged against.
/// </para>
/// </remarks>
public static class PackageProvenance
{
    /// <summary>Rewrites a document's origins to name <paramref name="package"/>.</summary>
    /// <param name="document">The parsed document.</param>
    /// <param name="package">The package it was read from.</param>
    public static PolicyDocument Qualify(PolicyDocument document, InstalledPipeline package)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(package);

        return new PolicyDocument
        {
            // The path is qualified too, and not only the origins. Seal
            // violations are reported from the document's own FilePath rather
            // than from an origin, so leaving it absolute printed
            // '%LOCALAPPDATA%\Preflight\pipelines\projecta\1.4.0\...' — the
            // account name of whoever ran the tool — into a message that
            // reaches a CI log. No absolute install path may appear in output,
            // and there are two doors into it: the origin of a policy value,
            // and the document's own path. This is the second.
            //
            // Safe because FilePath is identity and display from here on: the
            // loader has already resolved the whole 'extends' chain by the time
            // this runs, and every document from the package is qualified the
            // same way, so the comparisons the seal validator makes still line
            // up.
            FilePath = Describe(package, document.FilePath),
            Root = Qualify(document.Root, package.Name, package.Version.ToString()),
        };
    }

    /// <summary>
    /// How a package's file is named in a chain, a header or a history record.
    /// </summary>
    /// <remarks>
    /// The absolute install path is deliberately not used. It carries the
    /// account name of whoever ran the tool, and these strings reach the NDJSON
    /// history and a SARIF document that a review pipeline posts onto a merge
    /// request.
    /// </remarks>
    /// <param name="package">The package.</param>
    /// <param name="path">The absolute path of a file inside it.</param>
    public static string Describe(InstalledPipeline package, string path)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(path);

        var root = Path.TrimEndingDirectorySeparator(package.Root.FullName);

        var relative = path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? path[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : Path.GetFileName(path);

        return $"{package.Name}@{package.Version}/{relative.Replace('\\', '/')}";
    }

    /// <remarks>
    /// An <c>is</c> and a cast rather than a switch expression, for the reason
    /// <c>InspectionCommandHandlers.DescribeOrigin</c> writes down about the
    /// same shape: <see cref="PolicyNode"/> is a closed hierarchy of two, and a
    /// switch expression still demands a discard it cannot prove unreachable —
    /// which is a permanent hole in the branch count over a line nothing can
    /// enter.
    ///
    /// What that costs is a third variant added later reaching the cast and
    /// throwing, so the guard lives in a test:
    /// <c>PackageProvenanceTests</c> asserts the hierarchy is still exactly two,
    /// and it fails the day somebody widens it.
    /// </remarks>
    private static PolicyNode Qualify(PolicyNode node, string pipeline, string version) =>
        node is PolicyNode.ObjectNode objectNode
            ? new PolicyNode.ObjectNode(
                objectNode.Members.ToDictionary(
                    member => member.Key,
                    member => Qualify(member.Value, pipeline, version),
                    StringComparer.Ordinal))
            : new PolicyNode.Leaf(new PolicyValue<object?>
            {
                Entries =
                [
                    .. ((PolicyNode.Leaf)node).Value.Entries.Select(
                        entry => new PolicyValueEntry<object?>(
                            entry.Value, new PolicyOrigin.FromPackage(pipeline, version, entry.Origin))),
                ],
            });
}
