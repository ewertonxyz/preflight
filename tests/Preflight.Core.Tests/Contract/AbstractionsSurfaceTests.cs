namespace Preflight.Core.Tests.Contract;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;

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
/// though it is consumed by the tool and never delivered to the rule. That
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
    /// contract writes it. The tool never inspects the value — it only
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

    /// <summary>
    /// The volume probe stays one member wide.
    /// </summary>
    /// <remarks>
    /// It exists as a separate interface precisely because a member added to
    /// one every plugin implements breaks every compiled plugin. That argument
    /// is worth nothing if this one grows instead: the next capability is
    /// another new type, not a second method here.
    /// </remarks>
    [Fact]
    public void IVolumeProbe_ExposesExactlyProbeAsync()
    {
        MethodNamesOf<IVolumeProbe>().ShouldBe(["ProbeAsync"]);

        typeof(IFileSystem).IsAssignableFrom(typeof(IVolumeProbe)).ShouldBeFalse(
            "Reading a volume is a capability a host may offer, not a kind of file system.");
    }

    /// <summary>
    /// The environment probe stays one member wide, and stays synchronous.
    /// </summary>
    /// <remarks>
    /// The signature is the decision. A volume probe measures a path the
    /// workspace declared, which can sit on a dead network share, so it is
    /// asynchronous and cancellable; an environment block is a dictionary this
    /// process was handed at start-up, so there is nothing to wait for and an
    /// asynchronous signature would promise a cancellation that never arrives.
    /// Copying the neighbouring shape would be the easy mistake, and this is
    /// where it is refused.
    /// </remarks>
    [Fact]
    public void IEnvironmentProbe_ExposesExactlyRead_Synchronously()
    {
        MethodNamesOf<IEnvironmentProbe>().ShouldBe(["Read"]);

        var read = typeof(IEnvironmentProbe).GetMethod("Read")!;

        read.ReturnType.ShouldBe(typeof(string));
        read.GetParameters().Select(parameter => parameter.ParameterType).ShouldBe([typeof(string)]);

        typeof(IVolumeProbe).IsAssignableFrom(typeof(IEnvironmentProbe)).ShouldBeFalse(
            "Measuring a disk and reading an environment are not one responsibility.");
    }

    /// <remarks>
    /// A record and not a struct. It is returned through a nullable reference —
    /// "no measurement" is a specified answer the rule branches on — and a
    /// nullable value type would box on every call to say so.
    /// </remarks>
    [Fact]
    public void VolumeSpace_CarriesExactlyItsThreeMembers()
    {
        PropertyNamesOf<VolumeSpace>().ShouldBe(
            ["VolumeName", "TotalBytes", "AvailableBytes"],
            ignoreOrder: true);

        typeof(VolumeSpace).IsValueType.ShouldBeFalse();
        typeof(VolumeSpace).GetMethod("<Clone>$").ShouldNotBeNull("VolumeSpace is a record.");
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
