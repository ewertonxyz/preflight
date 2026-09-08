namespace Preflight.Core.Tests.Contract;

using System.Reflection;
using System.Runtime.CompilerServices;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;

/// <summary>
/// Pins the exact member set of <see cref="RuleContext"/> — including the
/// service that is deliberately absent from it.
/// </summary>
/// <remarks>
/// <see cref="IChangeSource"/> is sometimes counted among the services a rule
/// receives, and it is not one: it is consumed by the tool to populate
/// <c>ChangedFiles</c>, never delivered to the rule. This test exists so that
/// the apparent inconsistency is never resolved by wiring it into
/// <see cref="RuleContext"/>.
/// </remarks>
public sealed class RuleContextTests
{
    [Fact]
    public void RuleContext_ExposesEightRequiredMembersAndOneOptionalProbe_AndHasNoIChangeSourceProperty()
    {
        var properties = typeof(RuleContext).GetProperties()
            .ToDictionary(property => property.Name, property => property.PropertyType);

        properties.Keys.ShouldBe(
            [
                "WorkspaceRoot",
                "Stage",
                "Target",
                "ChangedFiles",
                "Policy",
                "Logger",
                "FileSystem",
                "Processes",
                "Volumes",
            ],
            ignoreOrder: true);

        properties["WorkspaceRoot"].ShouldBe(typeof(DirectoryInfo));
        properties["Stage"].ShouldBe(typeof(ValidationStage));
        properties["Target"].ShouldBe(typeof(BuildTarget));
        properties["ChangedFiles"].ShouldBe(typeof(IReadOnlyList<ChangedFile>));
        properties["Policy"].ShouldBe(typeof(IPolicyReader));
        properties["Logger"].ShouldBe(typeof(IRuleLogger));
        properties["FileSystem"].ShouldBe(typeof(IFileSystem));
        properties["Processes"].ShouldBe(typeof(IProcessRunner));
        properties["Volumes"].ShouldBe(typeof(IVolumeProbe));

        properties.Values.ShouldNotContain(typeof(IChangeSource));
    }

    /// <summary>
    /// The ninth member is the only one that is not required.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the volume probe arrived the way it did. A rule
    /// author builds a context by hand in their own unit tests, and a required
    /// member added here would break every one of those — to serve a capability
    /// exactly one rule needs. A rule that finds it absent reports that it
    /// checked nothing instead.
    /// </remarks>
    [Fact]
    public void RuleContext_Volumes_IsNotRequired()
    {
        typeof(RuleContext).GetProperty("Volumes")!
            .GetCustomAttribute<RequiredMemberAttribute>()
            .ShouldBeNull("A required member here breaks every plugin author's own tests.");

        typeof(RuleContext).GetProperty("FileSystem")!
            .GetCustomAttribute<RequiredMemberAttribute>()
            .ShouldNotBeNull("The four services every rule can count on stay required.");
    }
}
