namespace Preflight.Core.Policy;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

/// <summary>
/// One JSON policy file, parsed into a <see cref="PolicyNode"/> tree with
/// per-leaf file/line provenance already attached — before <c>extends</c> is
/// resolved (<see cref="PolicyLoader"/>) and before anything is validated
/// (<see cref="PolicyValidator"/>).
/// </summary>
/// <remarks>
/// Comments and trailing commas are allowed, because a production's policy file
/// is edited by humans who need to record why a limit is what it is. Capturing
/// line numbers has to happen here, during the read: a materialised
/// <see cref="JsonDocument"/> does not retain source line numbers once parsing
/// is done, so provenance is built value-by-value as the reader advances, not
/// bolted on afterward.
/// </remarks>
public sealed class PolicyDocument
{
    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public required string FilePath { get; init; }

    public required PolicyNode Root { get; init; }

    public static PolicyDocument Parse(string json, string filePath)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var lineStarts = ComputeLineStarts(bytes);

        var reader = new Utf8JsonReader(bytes, ReaderOptions);
        reader.Read();

        var root = ParseNode(ref reader, filePath, lineStarts);

        return new PolicyDocument { FilePath = filePath, Root = root };
    }

    public bool TryGetRaw(string path, out object? value) => TryGetRaw(path.Split('.'), out value);

    public bool TryGetRaw(IReadOnlyList<string> path, out object? value)
    {
        if (Root.TryGetPath(path, out var node) && node is PolicyNode.Leaf leaf)
        {
            value = leaf.Value.Value;
            return true;
        }

        value = null;
        return false;
    }

    private static PolicyNode ParseNode(ref Utf8JsonReader reader, string filePath, long[] lineStarts)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var members = new Dictionary<string, PolicyNode>();
            reader.Read();

            while (reader.TokenType != JsonTokenType.EndObject)
            {
                var key = reader.GetString()!;
                reader.Read();
                members[key] = ParseNode(ref reader, filePath, lineStarts);
                reader.Read();
            }

            return new PolicyNode.ObjectNode(members);
        }

        var origin = new PolicyOrigin.FromFile(filePath, LineNumberAt(reader.TokenStartIndex, lineStarts));
        var value = ParseRawValue(ref reader);

        return new PolicyNode.Leaf(PolicyValue.Initial(value, origin));
    }

    /// <summary>
    /// Maps the reader's current value token to a raw CLR value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pure dispatch: every arm is a constant, a single reader call, or a
    /// delegation to one of the measured helpers below. No decision that can be
    /// wrong lives in this method, which is what makes excluding it from
    /// coverage honest rather than convenient — the logic it dispatches to is
    /// measured on its own, and every arm here is still exercised behaviourally
    /// by the parsing tests.
    /// </para>
    /// <para>
    /// It is excluded because the final arm cannot be reached at all, and that
    /// was measured rather than assumed: every token-less input — empty,
    /// whitespace-only, comment-only — throws out of
    /// <see cref="Utf8JsonReader.Read"/> before this method is entered, so the
    /// reader never hands it a <see cref="JsonTokenType.None"/>, and a
    /// well-formed stream never puts a structural token in a value slot.
    /// Reaching it would mean this parser walked the reader wrongly, which is a
    /// defect in this file rather than in anyone's policy — hence
    /// <see cref="UnreachableException"/> rather than the
    /// <see cref="JsonException"/> that <c>PolicyLoader</c> would translate
    /// into a configuration error and blame on the user. The arm is kept, not
    /// folded into a silent fallback, precisely so that a future mis-walk fails
    /// loudly instead of producing a null leaf nobody notices.
    /// </para>
    /// </remarks>
    [ExcludeFromCodeCoverage]
    private static object? ParseRawValue(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => ReadNumber(ref reader),
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.Null => null,
        JsonTokenType.StartArray => ReadArray(ref reader),
        _ => throw new UnreachableException(
            $"Unexpected token '{reader.TokenType}' while reading a policy value."),
    };

    private static object ReadNumber(ref Utf8JsonReader reader) =>
        // The cast to object matters: without it, the conditional operator
        // unifies long and double to their common type (double), silently
        // turning every whole number in the document into a boxed double
        // instead of a boxed long.
        reader.TryGetInt64(out var integer) ? (object)integer : reader.GetDouble();

    private static object?[] ReadArray(ref Utf8JsonReader reader)
    {
        var items = new List<object?>();
        reader.Read();

        while (reader.TokenType != JsonTokenType.EndArray)
        {
            items.Add(ParseRawValue(ref reader));
            reader.Read();
        }

        // An all-string array keeps its element type, so a rule reading a
        // string list gets one without casting every element itself.
        return items.OfType<string>().Count() == items.Count
            ? items.Cast<string>().ToArray()
            : [.. items];
    }

    private static long[] ComputeLineStarts(byte[] bytes)
    {
        var starts = new List<long> { 0 };

        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                starts.Add(i + 1);
            }
        }

        return [.. starts];
    }

    private static int LineNumberAt(long byteOffset, long[] lineStarts)
    {
        var index = Array.BinarySearch(lineStarts, byteOffset);

        if (index < 0)
        {
            index = ~index - 1;
        }

        return index + 1;
    }
}
