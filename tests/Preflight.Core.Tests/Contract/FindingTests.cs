namespace Preflight.Core.Tests.Contract;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;

/// <summary>
/// Fixes the defaults of <see cref="Finding"/>, including the property it
/// deliberately does not have.
/// </summary>
/// <remarks>
/// a <see cref="Finding"/> has no <c>Severity</c>.
/// Severity belongs to the rule, not to an individual finding — a
/// <c>RuleOutcome.Warned()</c> whose findings disagreed on severity would be an
/// internally incoherent object the reporter would have to arbitrate.
/// </remarks>
public sealed class FindingTests
{
    [Fact]
    public void Finding_WhenOnlyMessageIsSet_LeavesOptionalMembersNull()
    {
        var finding = new Finding { Message = "The file exceeds the configured size limit." };

        finding.Location.ShouldBeNull();
        finding.Expected.ShouldBeNull();
        finding.Actual.ShouldBeNull();
        finding.Remediation.ShouldBeNull();
    }

    [Fact]
    public void Finding_DoesNotExposeASeverityProperty()
    {
        typeof(Finding).GetProperty("Severity").ShouldBeNull();
    }
}
