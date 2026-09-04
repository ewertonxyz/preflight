namespace Preflight.Cli.Pipelines;

using Preflight.Abstractions.Services;
using Preflight.Cli.Policy;
using Preflight.Core.Policy;

/// <summary>
/// The checkout's <c>preflight.base.json</c>, parsed once per invocation.
/// </summary>
/// <remarks>
/// <para>
/// Two independent questions are answered from this one file — which pipeline
/// the checkout declares itself to be, and which package versions it accepts —
/// and each used to open and parse it for itself. Two reads of one file can
/// disagree, because anything may write between them, and a run whose two
/// halves read different documents reports on a configuration that never
/// existed. It is the argument the install root is resolved once for.
/// </para>
/// <para>
/// The document is carried rather than the two answers, because the callers do
/// not agree on one of them: reading <c>requiresPipeline</c> asks whether the
/// same file also names a pipeline, and <c>run --pipeline x</c> and
/// <c>pipeline use x</c> answer that differently on purpose. Precomputing the
/// requirement here would force one answer on both.
/// </para>
/// <para>
/// A missing file is <see cref="Absent"/> and not an error. A workspace can be
/// validated on descriptor defaults alone, and a checkout that declares
/// nothing is a checkout, not a mistake.
/// </para>
/// </remarks>
/// <param name="Document">The parsed document, or <see langword="null"/> when the file is absent.</param>
/// <param name="Path">Where the file is, or would be. Named in every refusal this file produces.</param>
public sealed record CheckoutDocument(PolicyDocument? Document, string Path)
{
    /// <summary>
    /// Reads and parses the base document, if the checkout has one.
    /// </summary>
    /// <remarks>
    /// Synchronously, because every caller is synchronous and this is one small
    /// document read once per invocation. Making the selection path async to
    /// read it would add a state machine to six commands in exchange for
    /// nothing.
    /// </remarks>
    /// <param name="workspaceRoot">The workspace.</param>
    /// <param name="fileSystem">Read access to it.</param>
    /// <exception cref="System.Text.Json.JsonException">The file is not a policy document.</exception>
    public static CheckoutDocument Read(DirectoryInfo workspaceRoot, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var path = System.IO.Path.Combine(workspaceRoot.FullName, PolicyResolution.BaseFileName);

        if (!fileSystem.FileExists(path))
        {
            return new CheckoutDocument(null, path);
        }

        return new CheckoutDocument(
            PolicyDocument.Parse(
                fileSystem.ReadAllTextAsync(path, CancellationToken.None).GetAwaiter().GetResult(),
                path),
            path);
    }

    /// <summary>
    /// The document of a workspace holding no base file.
    /// </summary>
    /// <remarks>
    /// A value rather than a null, so that a command which never reads a
    /// checkout carries the same type as one that did.
    /// </remarks>
    public static CheckoutDocument Absent { get; } = new(null, string.Empty);
}
