namespace Preflight.Rules;

/// <summary>
/// One independent thing that can be wrong with a set of paths.
/// </summary>
/// <remarks>
/// <para>
/// The extension point that keeps a fifth check from becoming a fifth branch. A
/// rule performing four unrelated inspections with four private methods and a
/// chain of conditionals grows a new arm every time somebody thinks of another
/// one, and the policy has no way to turn a single inspection off without a
/// boolean per inspection. Behind this interface, a check is a class and the
/// policy selects it by name.
/// </para>
/// <para>
/// Internal, and a plugin author cannot implement it. That is not an oversight:
/// a plugin that wanted to add a path check writes a rule, which is the
/// extension point the contract actually offers. This one exists to keep one
/// rule's four inspections apart from each other.
/// </para>
/// </remarks>
internal interface IPathPortabilityCheck
{
    /// <summary>
    /// The name the policy selects this check by.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Inspects every path and reports what is wrong with it.
    /// </summary>
    /// <param name="paths">
    /// The changed paths, relative to the workspace root and written with
    /// forward slashes.
    /// </param>
    /// <param name="limits">The lengths the policy accepts.</param>
    /// <returns>
    /// One entry per defect found, in the order the paths were given. Empty
    /// when this check has nothing to say.
    /// </returns>
    IReadOnlyList<PathDefect> Inspect(IReadOnlyList<string> paths, PathPortabilityLimits limits);
}
