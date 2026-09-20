using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

public class ExportAndUnsetTests
{
    [Fact]
    public void ExportAssignsAndListsTheName()
    {
        using ShellHarness harness = new();

        Assert.Equal("export x=v\n", harness.Run("export x=v; export").Stdout);
    }

    [Fact]
    public void APlainAssignmentIsNotListed()
    {
        using ShellHarness harness = new();

        Assert.Equal(string.Empty, harness.Run("x=v; export").Stdout);
    }

    [Fact]
    public void ExportMarksAnExistingVariable()
    {
        using ShellHarness harness = new();

        Assert.Equal("export x=v\n", harness.Run("x=v; export x; export").Stdout);
    }

    [Fact]
    public void ExportListsEveryMarkedNameInOrder()
    {
        using ShellHarness harness = new();

        Assert.Equal("export a=1\nexport b=2\n", harness.Run("export b=2; export a=1; export").Stdout);
    }

    [Fact]
    public void ExportMinusNUnmarksButKeepsTheValue()
    {
        using ShellHarness harness = new();

        Assert.Equal("v\n", harness.Run("export x=v; export -n x; echo $x; export").Stdout);
    }

    [Fact]
    public void UnsetRemovesTheValue()
    {
        using ShellHarness harness = new();

        Assert.Equal("[]\n", harness.Run("x=v; unset x; echo \"[$x]\"").Stdout);
    }

    [Fact]
    public void UnsetRemovesTheExportMarkToo()
    {
        using ShellHarness harness = new();

        Assert.Equal(string.Empty, harness.Run("export x=v; unset x; export").Stdout);
    }

    [Fact]
    public void UnsetOfAnUnknownNameSucceeds()
    {
        using ShellHarness harness = new();

        Assert.Equal(0, harness.Run("unset nothing").ExitCode);
    }
}
