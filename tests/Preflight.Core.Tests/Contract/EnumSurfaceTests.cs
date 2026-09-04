namespace Preflight.Core.Tests.Contract;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Pins the exact member set of every closed enum a plugin can see.
/// </summary>
/// <remarks>
/// <c>ValidationStage</c> is closed to plugins: a plugin cannot add a stage,
/// because the stage determines the shape of <c>RuleContext</c> and the engine
/// has no way to populate one it does not know about. Removing or renaming an
/// enum member is a breaking change to the plugin contract, and pinning the
/// current set here is what turns that change into a failing test instead of a
/// silent one.
/// </remarks>
public sealed class EnumSurfaceTests
{
    [Fact]
    public void ValidationStage_DefinesExactlyWorkspacePreSubmitAndBuildReadiness()
    {
        Enum.GetNames<ValidationStage>().ShouldBe(
            ["Workspace", "PreSubmit", "BuildReadiness"],
            ignoreOrder: true);
    }

    [Fact]
    public void RuleStatus_DefinesExactlyTheSixDesignStatuses()
    {
        Enum.GetNames<RuleStatus>().ShouldBe(
            ["Passed", "Warning", "Failed", "Skipped", "NotApplicable", "Errored"],
            ignoreOrder: true);
    }

    [Fact]
    public void Severity_DefinesExactlyInformationWarningError()
    {
        Enum.GetNames<Severity>().ShouldBe(
            ["Information", "Warning", "Error"],
            ignoreOrder: true);
    }

    [Fact]
    public void ChangeKind_DefinesExactlyAddedModifiedDeletedRenamed()
    {
        Enum.GetNames<ChangeKind>().ShouldBe(
            ["Added", "Modified", "Deleted", "Renamed"],
            ignoreOrder: true);
    }
}
