namespace Preflight.Core.Policy;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;

/// <summary>
/// Resolves one policy file's <c>extends</c> chain into a single, fully merged
/// <see cref="PolicyDocument"/>.
/// </summary>
/// <remarks>
/// Reads through <see cref="IFileSystem"/> — already public in
/// <c>Preflight.Abstractions</c> — injected directly here rather than delivered
/// via <c>RuleContext</c>, which does not exist yet at policy-load time. That
/// keeps the loader testable against a fake, without touching disk, the same
/// way the built-in rules are.
///
/// <c>extends</c> is a single string per file, so this is always a linear chain
/// — there is no diamond shape to resolve, only a self-reference or a longer
/// cycle to detect. Detection tracks the ordered chain of files visited, not
/// just a set, so a cycle error can list the full chain in order rather than
/// just say "cycle detected".
/// </remarks>
public sealed class PolicyLoader
{
    private readonly IFileSystem _fileSystem;

    public PolicyLoader(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Resolves <paramref name="entryPath"/> and its <c>extends</c> ancestors
    /// into one merged document, together with the chain of files that produced
    /// it.
    /// </summary>
    /// <remarks>
    /// The chain comes back in <b>application order</b> — the furthest ancestor
    /// first, the entry file last — which is the order precedence is defined
    /// in, and the order <c>explain</c> and the console header print. The
    /// traversal builds it the other way round, entry first, because that is
    /// the order a cycle has to be reported in; the reversal happens here,
    /// once, rather than at each of the three places that display it.
    /// </remarks>
    public async Task<PolicyLoadResult> LoadAsync(string entryPath, CancellationToken cancellationToken)
    {
        var chain = new List<string>();
        var documents = new List<PolicyDocument>();
        var document = await LoadChainAsync(entryPath, chain, documents, cancellationToken);

        chain.Reverse();
        documents.Reverse();

        return new PolicyLoadResult
        {
            Document = document,
            Chain = chain,
            Documents = documents,
        };
    }

    /// <summary>
    /// Converts the reader's zero-based line number to the one-based number
    /// every editor shows.
    /// </summary>
    /// <remarks>
    /// The null branch exists only to satisfy
    /// <see cref="JsonException.LineNumber"/> being declared nullable. Measured
    /// against every token-less and malformed input this loader can receive,
    /// <see cref="Utf8JsonReader"/> always populates it, so that branch is
    /// unreachable here — excluded rather than covered by a fabricated case.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    private static int? OneBasedLine(JsonException exception) =>
        exception.LineNumber is { } line ? (int)line + 1 : null;

    private async Task<PolicyDocument> LoadChainAsync(
        string path,
        List<string> chain,
        List<PolicyDocument> documents,
        CancellationToken cancellationToken)
    {
        var absolutePath = Path.GetFullPath(path);

        if (chain.Contains(absolutePath, StringComparer.OrdinalIgnoreCase))
        {
            chain.Add(absolutePath);

            throw new PolicyValidationException([
                new PolicyValidationError(
                    $"Cycle detected in 'extends' chain: {string.Join(" -> ", chain)}",
                    absolutePath,
                    null,
                    "extends"),
            ]);
        }

        chain.Add(absolutePath);

        if (!_fileSystem.FileExists(absolutePath))
        {
            throw new PolicyValidationException([
                new PolicyValidationError(
                    $"'extends' target does not exist: {absolutePath}",
                    absolutePath,
                    null,
                    "extends"),
            ]);
        }

        var json = await _fileSystem.ReadAllTextAsync(absolutePath, cancellationToken);

        PolicyDocument document;

        try
        {
            document = PolicyDocument.Parse(json, absolutePath);
        }
        catch (JsonException exception)
        {
            throw new PolicyValidationException([
                new PolicyValidationError(
                    $"Malformed JSON in '{absolutePath}': {exception.Message}",
                    absolutePath,
                    OneBasedLine(exception),
                    null),
            ]);
        }

        // Collected before the recursion and before the merge, so what is kept
        // is what this file itself said rather than what it says after its
        // ancestor's values are folded in.
        documents.Add(document);

        if (!document.TryGetRaw("extends", out var extendsRaw) || extendsRaw is not string extendsRelative)
        {
            return document;
        }

        // Not null-checked, and not defensively defaulted either: the
        // FileExists guard above already established that this path names a
        // file, and a file path always has a parent directory. GetDirectoryName
        // returns null only for a bare filesystem root, which no FileExists can
        // truthfully report. A "?? \".\"" here would be a branch no real
        // filesystem can take, reachable only by a test double telling a lie.
        var extendsPath = Path.Combine(Path.GetDirectoryName(absolutePath)!, extendsRelative);
        var ancestor = await LoadChainAsync(extendsPath, chain, documents, cancellationToken);

        return new PolicyDocument
        {
            FilePath = document.FilePath,
            Root = PolicyNode.Merge(ancestor.Root, document.Root),
        };
    }
}
