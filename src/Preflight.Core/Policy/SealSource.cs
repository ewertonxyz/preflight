namespace Preflight.Core.Policy;

/// <summary>
/// Which file sealed a path, and with which pattern.
/// </summary>
/// <remarks>
/// No line, and that is a decision rather than an omission.
/// <c>PolicyDocument.ReadArray</c> collapses an array into a single leaf, so
/// every entry of one <c>sealed</c> block would share one number — twenty seals
/// pointing at the same place. The pattern is what tells the reader which one
/// they hit, and the error already carries the line of the file that violated
/// it, which is the line somebody has to edit.
/// </remarks>
/// <param name="FilePath">The file that declared the seal.</param>
/// <param name="Pattern">The entry as it was written.</param>
public sealed record SealSource(string FilePath, string Pattern);
