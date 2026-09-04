namespace Preflight.Cli.Tests;

using System.Reflection;

/// <summary>
/// Guards the direction of the dependency between Preflight.Core and the
/// command line that hosts it.
/// </summary>
/// <remarks>
/// <para>
/// <c>Preflight.Core</c> does not depend on <c>Preflight.Cli</c> and knows
/// nothing about output formatting. What that buys is a second deployment: if
/// invocation volume ever made a process per validation untenable, Core could be
/// hosted as a library inside another process without a rewrite.
/// </para>
/// <para>
/// The moment Core reaches back into the CLI — for a console width, a colour
/// setting, an exit code constant — that option is gone, and it goes quietly. No
/// test fails, nothing looks wrong, and the cost only appears years later in a
/// conversation about scale.
/// </para>
/// </remarks>
public sealed class HostDependencyTests
{
    private const string CoreAssemblyName = "Preflight.Core";
    private const string RulesAssemblyName = "Preflight.Rules";

    /// <summary>
    /// The CLI ships as <c>preflight</c> so that the produced binary matches the
    /// command lines in the documentation, so its assembly name is not
    /// <c>Preflight.Cli</c>.
    /// </summary>
    private const string CliAssemblyName = "preflight";

    [Fact]
    public void Core_DoesNotReference_TheCli()
    {
        var offenders = ReferencedAssemblyNamesOf(CoreAssemblyName)
            .Where(name => name.Equals(CliAssemblyName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        offenders.ShouldBeEmpty(
            "The tool must stay hostable outside this executable.");
    }

    [Fact]
    public void Rules_DoesNotReference_TheCli()
    {
        var offenders = ReferencedAssemblyNamesOf(RulesAssemblyName)
            .Where(name => name.Equals(CliAssemblyName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        offenders.ShouldBeEmpty("A rule knows nothing about how its result is presented.");
    }

    // There is deliberately no test asserting that the CLI *does* reference Core
    // and Rules.
    //
    // GetReferencedAssemblies reads assembly metadata, and the C# compiler omits
    // references the code never uses. Preflight.Cli declares both ProjectReferences
    // today and neither appears in its metadata, because Program.Main does not yet
    // touch either assembly.
    //
    // That asymmetry is what makes the negative assertions above sound and a
    // positive one meaningless: an assembly cannot use a type without the
    // reference being emitted, so absence really does prove non-use. Presence
    // proves nothing until there is code. A structural claim about what a project
    // *declares* belongs to a csproj-level check, which is where the guard
    // in Preflight.Rules.Tests puts it.

    // There was a test here called Abstractions_IsReachableFromTheCli, and it was
    // deleted rather than repaired.
    //
    // It loaded Preflight.Abstractions by name and asserted the result was not
    // null, with a comment saying it guarded the delegation of plugin loading
    // . Assembly.Load either throws or returns something, so the assertion
    // could not fail — it guarded nothing, while claiming to guard the invariant
    // the whole plugin model rests on, which is worse than no test at all.
    //
    // What actually guards it is PluginLoadContextTests: the contract assembly
    // resolved through a plugin's load context is asserted to be the same
    // Assembly *instance* the host holds, and the negative control puts a copy
    // of Preflight.Abstractions.dll beside the plugin so that a broken resolver
    // and a correct one stop behaving identically.

    private static string[] ReferencedAssemblyNamesOf(string assemblyName)
    {
        var assembly = Assembly.Load(new AssemblyName(assemblyName));

        return [.. assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()];
    }
}

/// <summary>
/// Keeps the terminal library inside the one namespace allowed to draw on a
/// terminal.
/// </summary>
/// <remarks>
/// <para>
/// Spectre.Console writes ANSI, and so does the console reporter. Two systems
/// deciding the same bytes is how a golden file stops being the truth: the
/// snapshot suite is the only thing that holds the report's exact output, and it
/// cannot arbitrate between two writers. So the library is confined to
/// <c>Preflight.Cli.Interactive</c>, where nothing is snapshotted and nothing is
/// meant to be.
/// </para>
/// <para>
/// No existing guard sees this. The architecture check above filters by the
/// <c>Preflight.</c> prefix and would not notice a third-party package arriving
/// anywhere at all. Confining the library to one namespace and asserting it here
/// was the condition on taking the dependency at all — the alternative on the
/// table was writing the picker by hand, and a terminal library that had spread
/// past the picker would be the thing that made that trade a bad one.
/// </para>
/// </remarks>
public sealed class InteractiveBoundaryTests
{
    private const string SpectreAssemblyName = "Spectre.Console";

    private const string InteractiveNamespace = "Preflight.Cli.Interactive";

    [Fact]
    public void SpectreConsole_IsUsedOnlyInsideTheInteractiveNamespace()
    {
        var offenders = typeof(PreflightCommandLine).Assembly
            .GetTypes()
            .Where(type => !string.Equals(
                type.Namespace, InteractiveNamespace, StringComparison.Ordinal))
            .Where(MentionsSpectre)
            .Select(type => type.FullName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty(
            "Only Preflight.Cli.Interactive may reach for a terminal library; " +
            "anything else puts a second ANSI writer beside the reporter.");
    }

    /// <remarks>
    /// The negative control, and it is not optional. Without it a detector that
    /// finds nothing anywhere — a renamed assembly, a signature walk that
    /// stopped returning anything — passes the test above and keeps passing it
    /// after the library has spread everywhere.
    /// </remarks>
    [Fact]
    public void TheDetector_FindsTheOneTypeThatIsAllowedToUseIt() =>
        typeof(Preflight.Cli.Interactive.SpectrePipelinePicker)
            .ShouldSatisfyAllConditions(
                type => MentionsSpectre(type).ShouldBeTrue(),
                type => type.Namespace.ShouldBe(InteractiveNamespace));

    /// <remarks>
    /// Signatures rather than IL. A type that merely calls into Spectre without
    /// naming it in a signature would slip through, and walking IL to catch that
    /// is the shape of guard this repository has already refused once as a test
    /// that looks like a guard and is not. What this does catch is the arrival
    /// that actually happens: a field, a parameter or a return type, which is
    /// how a library gets adopted by a second component.
    /// </remarks>
    private static bool MentionsSpectre(Type type) =>
        Signatures(type).Any(referenced =>
            string.Equals(
                referenced.Assembly.GetName().Name, SpectreAssemblyName, StringComparison.Ordinal));

    private static IEnumerable<Type> Signatures(Type type)
    {
        const BindingFlags Everything =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(Everything))
        {
            yield return field.FieldType;
        }

        foreach (var property in type.GetProperties(Everything))
        {
            yield return property.PropertyType;
        }

        foreach (var method in type.GetMethods(Everything))
        {
            yield return method.ReturnType;

            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var constructor in type.GetConstructors(Everything))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }
}
