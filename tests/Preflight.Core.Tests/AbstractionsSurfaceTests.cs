namespace Preflight.Core.Tests;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Preflight.Abstractions;

/// <summary>
/// Pins the exact member set of every service interface a rule receives —
/// including the members that were deliberately left out.
/// </summary>
/// <remarks>
/// <para>
/// Notice what is absent: there is no <c>Error</c> on
/// <see cref="IRuleLogger"/>, and <see cref="IFileSystem"/> is read-only by
/// construction — the rule that this tool never writes to the workspace is
/// expressed in the type system rather than in a comment. A member added later
/// to either interface is exactly the kind of change the plugin version
/// contract prices as breaking for every external plugin.
/// </para>
/// <para>
/// <see cref="IChangeSource"/> is sometimes counted among those services, even
/// though it is consumed by the engine and never delivered to the rule. That
/// property is tested against <c>RuleContext</c> itself, in
/// <c>RuleContextTests</c>, not here.
/// </para>
/// <para>
/// The policy reader: the <c>[MaybeNullWhen(false)]</c> annotation on
/// <see cref="IPolicyReader.TryGetValue{T}"/> is not cosmetic — without it,
/// every caller would need a null-forgiving operator on the success branch. It
/// is metadata, invisible to an ordinary behavioural test, so only reflection
/// can pin it against being silently dropped.
/// </para>
/// </remarks>
public sealed class AbstractionsSurfaceTests
{
    [Fact]
    public void IValidationRule_ExposesExactlyDescriptorAndExecuteAsync()
    {
        PropertyNamesOf<IValidationRule>().ShouldBe(["Descriptor"]);
        MethodNamesOf<IValidationRule>().ShouldBe(["ExecuteAsync"]);
    }

    /// <summary>
    /// The optional interface of the fingerprint contract, and the fact that it
    /// is optional.
    /// </summary>
    /// <remarks>
    /// The second assertion is the load-bearing one. The plugin version
    /// contract prices a new member on <see cref="IValidationRule"/> as a major
    /// version that recompiles every plugin; a new type is a minor one. If
    /// somebody ever "simplifies" this by folding the method into
    /// <c>IValidationRule</c> with a default implementation, the test above
    /// this one fails and this remark explains why that is not a
    /// simplification.
    /// </remarks>
    [Fact]
    public void ICacheableRule_ExposesExactlyComputeFingerprintAsync()
    {
        MethodNamesOf<ICacheableRule>().ShouldBe(["ComputeFingerprintAsync"]);

        typeof(IValidationRule).IsAssignableFrom(typeof(ICacheableRule)).ShouldBeFalse(
            "A cacheable rule is a rule that also implements this, not a kind of rule. " +
            "A new member here is a major version of the plugin contract.");
    }

    /// <remarks>
    /// A readonly record struct with one member, exactly as the fingerprint
    /// contract writes it. The engine never inspects the value — it only
    /// compares it — so the shape is the whole contract, and a second member
    /// would be a second thing a rule author has to be told about.
    /// </remarks>
    [Fact]
    public void CacheFingerprint_IsAReadonlyStructCarryingExactlyItsValue()
    {
        PropertyNamesOf<CacheFingerprint>().ShouldBe(["Value"]);
        typeof(CacheFingerprint).IsValueType.ShouldBeTrue();
    }

    [Fact]
    public void IRuleLogger_ExposesExactlyDebugInfoAndWarn()
    {
        MethodNamesOf<IRuleLogger>().ShouldBe(["Debug", "Info", "Warn"], ignoreOrder: true);

        typeof(IRuleLogger).GetMethod("Error").ShouldBeNull(
            "A rule reports problems through Finding, not through the log.");
    }

    [Fact]
    public void IFileSystem_ExposesExactlyTheReadOnlyMembers()
    {
        MethodNamesOf<IFileSystem>().ShouldBe(
            [
                "FileExists",
                "DirectoryExists",
                "GetFileSize",
                "OpenRead",
                "ReadAllTextAsync",
                "ReadAllBytesAsync",
                "EnumerateFiles",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void IProcessRunner_ExposesExactlyRunAsync()
    {
        MethodNamesOf<IProcessRunner>().ShouldBe(["RunAsync"]);
    }

    [Fact]
    public void IChangeSource_ExposesExactlyNameAndGetChangesAsync()
    {
        PropertyNamesOf<IChangeSource>().ShouldBe(["Name"]);
        MethodNamesOf<IChangeSource>().ShouldBe(["GetChangesAsync"]);
    }

    [Fact]
    public void IPolicyReader_TryGetValue_OutParameterCarriesMaybeNullWhenFalse()
    {
        var tryGetValue = typeof(IPolicyReader).GetMethod("TryGetValue");

        tryGetValue.ShouldNotBeNull();
        tryGetValue.IsGenericMethodDefinition.ShouldBeTrue();
        tryGetValue.GetGenericArguments().Length.ShouldBe(1);

        var valueParameter = tryGetValue.GetParameters().Single(parameter => parameter.Name == "value");

        valueParameter.IsOut.ShouldBeTrue();

        var attribute = valueParameter.GetCustomAttribute<MaybeNullWhenAttribute>();

        attribute.ShouldNotBeNull();
        attribute.ReturnValue.ShouldBeFalse();
    }

    private static string[] PropertyNamesOf<T>() =>
        [.. typeof(T).GetProperties().Select(property => property.Name)];

    private static string[] MethodNamesOf<T>() =>
        [.. typeof(T).GetMethods().Where(method => !method.IsSpecialName).Select(method => method.Name)];
}
