namespace Preflight.Core.Tests.Contract;

using Preflight.Abstractions.Services;

/// <summary>
/// Fixes the defaults of <see cref="ProcessRequest"/> and the positional
/// mapping of <see cref="ProcessResult"/>, plus the same shallow-equality trap
/// documented in <see cref="RuleDescriptorTests"/>.
/// </summary>
public sealed class ProcessContractTests
{
    [Fact]
    public void ProcessRequest_WhenOnlyFileNameIsSet_AppliesTheDocumentedDefaults()
    {
        var request = new ProcessRequest { FileName = "dotnet" };

        request.Arguments.ShouldBeEmpty();
        request.WorkingDirectory.ShouldBeNull();
        request.Timeout.ShouldBeNull();
    }

    [Fact]
    public void ProcessRequest_TwoInstancesWithDifferentArgumentsListInstances_AreNotEqual()
    {
        var first = RequestWithArguments(["--flag"]);
        var second = RequestWithArguments(["--flag"]);

        first.ShouldNotBe(second);
    }

    [Fact]
    public void ProcessResult_ConstructedPositionally_MapsEachPositionToTheCorrectProperty()
    {
        var result = new ProcessResult(0, "STDOUT", "STDERR", TimeSpan.FromSeconds(2));

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldBe("STDOUT");
        result.StandardError.ShouldBe("STDERR");
        result.Duration.ShouldBe(TimeSpan.FromSeconds(2));
    }

    private static ProcessRequest RequestWithArguments(IReadOnlyList<string> arguments) =>
        new() { FileName = "dotnet", Arguments = arguments };
}
