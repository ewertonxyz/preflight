namespace Preflight.Rules;

using System.Globalization;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;

/// <summary>
/// Fails when a volume the workspace declares has less room left than the
/// manifest says a build needs.
/// </summary>
/// <remarks>
/// <para>
/// One system call, against a forty-minute build that dies on a full disk and
/// leaves a half-written output tree behind it. There is no cheaper check in
/// the set and none whose absence costs more.
/// </para>
/// <para>
/// No default floor, and that is the decision rather than an oversight. How
/// much room a build needs is a fact about a production's content and its
/// toolchain, and a number invented here would be the tool asserting something
/// nobody measured. With nothing declared, the rule reports that it checked
/// nothing.
/// </para>
/// </remarks>
public sealed class FreeSpaceRule : IValidationRule
{
    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.FreeSpace,
        DisplayName = "Free space",
        Stage = ValidationStage.Workspace,

        // Hangs off the toolchain rule because that is what establishes there
        // is a workspace worth measuring at all; nothing about free space
        // depends on the dependency rule beside it.
        DependsOn = [BuiltInRuleIds.Toolchain],
        DefaultBlocking = true,

        // Nothing depends on this rule, so gating would change nothing whatever
        // it said. Written out anyway, because the descriptor's own default is
        // true and a reader finding it inherited cannot tell a decision from an
        // omission.
        DefaultGating = false,
    };

    public async Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var read = await WorkspaceManifestRead.ReadAsync(context, cancellationToken);

        if (read.Malformed is { } malformed)
        {
            return RuleOutcome.Failed(malformed);
        }

        // A missing manifest is not reported here. The toolchain rule this one
        // depends on fails loudly on the same file, and saying it twice would
        // put one problem on two lines.
        if (read.Manifest is not { FreeSpace.Count: > 0 } manifest)
        {
            return RuleOutcome.NotApplicable();
        }

        if (context.Volumes is not { } volumes)
        {
            return RuleOutcome.NotApplicable();
        }

        var findings = new List<Finding>();
        var unmeasured = new List<string>();

        foreach (var requirement in manifest.FreeSpace)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var space = await MeasureAsync(volumes, context, requirement, cancellationToken);

            if (space is null)
            {
                unmeasured.Add(requirement.Path);

                continue;
            }

            if (space.AvailableBytes < requirement.MinimumBytes)
            {
                findings.Add(Describe(requirement, space));
            }
        }

        // A failure stands whatever else went unmeasured — a floor that was
        // provably missed is a fact. A pass does not: reporting one while a
        // declared path could not be measured would claim more than was
        // measured, which is the whole reason this status exists.
        if (findings.Count > 0)
        {
            return RuleOutcome.Failed([.. findings]);
        }

        return unmeasured.Count > 0 ? RuleOutcome.NotApplicable() : RuleOutcome.Passed();
    }

    /// <remarks>
    /// A probe that throws is a bad machine or an unreachable share, and
    /// neither is a broken workspace — so it reads as "not measured" rather
    /// than as a failure that would send the reader to free space they have
    /// plenty of. Cancellation is not swallowed: a deadline that expired is the
    /// tool's verdict to give.
    /// </remarks>
    private static async Task<VolumeSpace?> MeasureAsync(
        IVolumeProbe volumes,
        RuleContext context,
        FreeSpaceRequirement requirement,
        CancellationToken cancellationToken)
    {
        try
        {
            return await volumes.ProbeAsync(
                Path.Combine(context.WorkspaceRoot.FullName, requirement.Path),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <remarks>
    /// A floor larger than the whole volume gets a different remedy, because
    /// telling somebody to free space they could never free costs them an
    /// afternoon before they work out the number itself is wrong.
    /// </remarks>
    private static Finding Describe(FreeSpaceRequirement requirement, VolumeSpace space) => new()
    {
        Message = $"'{requirement.Path}' is on a volume with less room than the workspace needs.",
        Location = new FindingLocation(requirement.Path),
        Expected = $"at least {Bytes(requirement.MinimumBytes)} available",
        Actual = $"{Bytes(space.AvailableBytes)} available on {space.VolumeName}",
        Remediation = requirement.MinimumBytes > space.TotalBytes
            ? $"The declared minimum is larger than the volume holding '{requirement.Path}' " +
              $"({Bytes(space.TotalBytes)} in total). Move the path to a larger volume, or correct " +
              "'minimumBytes' in the workspace manifest."
            : $"Free space on {space.VolumeName}, or move '{requirement.Path}' to a volume with more room.",
    };

    /// <remarks>
    /// Grouped and invariant. A digit group separator that follows the machine's
    /// culture would make one repository produce two reports, and this number
    /// is compared by eye against the one in the manifest.
    /// </remarks>
    private static string Bytes(long value) =>
        $"{value.ToString("N0", CultureInfo.InvariantCulture)} bytes";
}
