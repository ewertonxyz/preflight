namespace Preflight.Core.Tests;

using Preflight.Abstractions;

/// <summary>
/// Pins the exact member set of <see cref="RuleContext"/> — including the
/// service that is deliberately absent from it.
/// </summary>
/// <remarks>
/// <see cref="IChangeSource"/> is sometimes counted among the services a rule
/// receives, and it is not one: it is consumed by the engine to populate
/// <c>ChangedFiles</c>, never delivered to the rule. This test exists so that
/// the apparent inconsistency is never resolved by wiring it into
/// <see cref="RuleContext"/>.
/// </remarks>
public sealed class RuleContextTests
{
    [Fact]
    public void RuleContext_ExposesExactlyTheEightRequiredMembers_AndHasNoIChangeSourceProperty()
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

        properties.Values.ShouldNotContain(typeof(IChangeSource));
    }
}
