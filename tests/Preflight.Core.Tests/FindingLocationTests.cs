namespace Preflight.Core.Tests;

using Preflight.Abstractions;

/// <summary>
/// Fixes the default shape of <see cref="FindingLocation"/>.
/// </summary>
public sealed class FindingLocationTests
{
    [Fact]
    public void FindingLocation_WhenOnlyRelativePathIsSet_LineAndColumnDefaultToNull()
    {
        var location = new FindingLocation("src/Foo.cs");

        location.Line.ShouldBeNull();
        location.Column.ShouldBeNull();
    }
}
