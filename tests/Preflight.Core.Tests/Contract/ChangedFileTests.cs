namespace Preflight.Core.Tests.Contract;

using Preflight.Abstractions.Model;

/// <summary>
/// Fixes the default shape of <see cref="ChangedFile"/> and documents the one
/// invariant the type does not enforce.
/// </summary>
/// <remarks>
/// <c>PreviousRelativePath</c> is only set when <c>Kind</c> is <c>Renamed</c> —
/// a convention stated in prose, not a constructor check. The second test below
/// does not assert a defect — it pins the current, permissive behaviour so that
/// adding validation here later is a deliberate decision instead of a
/// discovery.
/// </remarks>
public sealed class ChangedFileTests
{
    [Fact]
    public void ChangedFile_WithoutPreviousRelativePath_DefaultsToNull()
    {
        var changedFile = new ChangedFile("src/Foo.cs", ChangeKind.Added);

        changedFile.PreviousRelativePath.ShouldBeNull();
    }

    [Fact]
    public void ChangedFile_DoesNotEnforceThatPreviousRelativePathRequiresKindRenamed()
    {
        var changedFile = new ChangedFile("src/Foo.cs", ChangeKind.Added, "src/OldFoo.cs");

        changedFile.Kind.ShouldBe(ChangeKind.Added);
        changedFile.PreviousRelativePath.ShouldBe("src/OldFoo.cs");
    }
}
