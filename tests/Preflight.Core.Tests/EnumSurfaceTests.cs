namespace Preflight.Core.Tests;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Pins the exact member set of each closed enum in the rule-id contract
/// and 5.4.
/// </summary>
/// <remarks>
/// <c>ValidationStage</c> is closed by design — a
/// plugin cannot add a stage, because the stage determines the shape of
/// <c>RuleContext</c> and the engine has no way to populate a stage it does not
/// know about. the plugin version contract treats removing or renaming an enum
/// member as a breaking surface change; pinning the current set here is what
/// turns that change into a failing test instead of a silent one.
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
