namespace Preflight.Rules;

/// <summary>
/// The two numbers the length check compares against, read from policy once.
/// </summary>
/// <param name="MaxPathLength">
/// The longest acceptable path, relative to the workspace root, in characters.
/// </param>
/// <param name="MaxComponentLength">
/// The longest acceptable single directory or file name, in characters.
/// </param>
/// <remarks>
/// Read once, outside the loop, and handed to every check. Without it each
/// check would need the policy reader, which would mean every test of a check
/// arranging a policy in order to exercise something that has nothing to do
/// with configuration — and a policy read per path, which is a lookup per file
/// in a change set that can hold thousands.
/// </remarks>
internal sealed record PathPortabilityLimits(int MaxPathLength, int MaxComponentLength);
