namespace Preflight.Core.Tests.History;

using Preflight.Core.History;
using Preflight.Core.Policy;

/// <summary>
/// The file names of the history format.
/// </summary>
public sealed class HistoryPathsTests
{
    private static readonly DateTimeOffset August =
        new(2026, 8, 26, 14, 30, 0, TimeSpan.Zero);

    private static readonly EngineEnvironment Workstation = new()
    {
        ProcessorCount = 8,
        MachineName = "WKS-1234",
        ProcessId = 4242,
    };

    /// <remarks>
    /// Both literals are copied out of the history format, which draws them.
    /// </remarks>
    [Theory]
    [InlineData(HistoryMode.Shared, "2026-08.WKS-1234.ndjson")]
    [InlineData(HistoryMode.PerProcess, "2026-08.WKS-1234.4242.ndjson")]
    public void FileNameFor_ForEachMode_IsTheDocumentedName(HistoryMode mode, string expected) =>
        HistoryPaths.FileNameFor(new HistorySettings(".preflight/history", mode), Workstation, August)
            .ShouldBe(expected);

    /// <summary>
    /// The month is UTC, whatever the offset of the instant.
    /// </summary>
    /// <remarks>
    /// The instant below is the first of September in UTC and the
    /// thirty-first of August thirteen hours west of it; in local time two
    /// machines would file the same run under two different months, and
    /// <c>--since</c> would stop lining up across the boundary.
    /// </remarks>
    [Fact]
    public void FileNameFor_ForAnInstantThatCrossesTheMonthLocally_UsesUtc() =>
        HistoryPaths.FileNameFor(
                new HistorySettings(".preflight/history", HistoryMode.Shared),
                Workstation,
                new DateTimeOffset(2026, 8, 31, 20, 0, 0, TimeSpan.FromHours(-13)))
            .ShouldStartWith("2026-09.");

    /// <summary>
    /// A machine name that a file name cannot carry is reduced to one that can.
    /// </summary>
    /// <remarks>
    /// A domain-joined machine reports a fully qualified name, and the dot in it
    /// is the separator this file name is built out of.
    /// </remarks>
    [Theory]
    [InlineData("build-07.corp.example.com", "2026-08.build-07-corp-example-com.ndjson")]
    [InlineData("WKS_1234", "2026-08.WKS_1234.ndjson")]
    [InlineData("a/b\\c:d", "2026-08.a-b-c-d.ndjson")]
    public void FileNameFor_ForAMachineNameAFileNameCannotCarry_ReducesIt(string machine, string expected) =>
        HistoryPaths.FileNameFor(
                new HistorySettings(".preflight/history", HistoryMode.Shared),
                Workstation with { MachineName = machine },
                August)
            .ShouldBe(expected);

    /// <summary>
    /// A relative path resolves against the workspace, never the process
    /// directory.
    /// </summary>
    /// <remarks>
    /// Resolved against the current directory, the history would split according
    /// to where the build agent happened to be standing — and the report reads
    /// all of it back as one series.
    /// </remarks>
    [Fact]
    public void DirectoryFor_ForARelativePath_ResolvesAgainstTheWorkspaceRoot()
    {
        var workspace = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "preflight-paths"));

        HistoryPaths.DirectoryFor(workspace, new HistorySettings(".preflight/history", HistoryMode.Shared))
            .ShouldBe(Path.Combine(workspace.FullName, ".preflight/history"));
    }

    [Fact]
    public void DirectoryFor_ForARootedPath_LeavesItAlone()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "preflight-shared-history");

        HistoryPaths.DirectoryFor(
                new DirectoryInfo(Path.GetTempPath()),
                new HistorySettings(rooted, HistoryMode.Shared))
            .ShouldBe(rooted);
    }
}
