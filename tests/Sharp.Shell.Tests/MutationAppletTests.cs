using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

public class MutationAppletTests
{
    [Fact]
    public void MkdirCreatesADirectory()
    {
        using ShellHarness harness = new();

        harness.Run("mkdir made");

        Assert.True(Directory.Exists(Path.Combine(harness.Root, "made")));
    }

    [Fact]
    public void MkdirNeedsDashPForNestedPaths()
    {
        using ShellHarness harness = new();

        Assert.NotEqual(0, harness.Run("mkdir a/b").ExitCode);
        Assert.Equal(0, harness.Run("mkdir -p a/b").ExitCode);
        Assert.True(Directory.Exists(Path.Combine(harness.Root, "a", "b")));
    }

    [Fact]
    public void RmDeletesAFile()
    {
        using ShellHarness harness = new();
        harness.Write("f.txt", "");

        harness.Run("rm f.txt");

        Assert.False(File.Exists(Path.Combine(harness.Root, "f.txt")));
    }

    [Fact]
    public void RmNeedsDashRForDirectories()
    {
        using ShellHarness harness = new();
        harness.Write("dir/f.txt", "");

        Assert.NotEqual(0, harness.Run("rm dir").ExitCode);
        Assert.Equal(0, harness.Run("rm -r dir").ExitCode);
        Assert.False(Directory.Exists(Path.Combine(harness.Root, "dir")));
    }

    [Fact]
    public void RmDashFIgnoresAMissingFile()
    {
        using ShellHarness harness = new();

        Assert.NotEqual(0, harness.Run("rm missing").ExitCode);
        Assert.Equal(0, harness.Run("rm -f missing").ExitCode);
    }

    [Fact]
    public void MvRenamesAFile()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "body");

        harness.Run("mv a.txt b.txt");

        Assert.False(File.Exists(Path.Combine(harness.Root, "a.txt")));
        Assert.Equal("body", File.ReadAllText(Path.Combine(harness.Root, "b.txt")));
    }

    [Fact]
    public void MvMovesIntoADirectory()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "body");
        harness.Run("mkdir dir");

        harness.Run("mv a.txt dir");

        Assert.Equal("body", File.ReadAllText(Path.Combine(harness.Root, "dir", "a.txt")));
    }

    [Fact]
    public void MvMovesSeveralSourcesIntoADirectory()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "first");
        harness.Write("b.txt", "second");
        harness.Run("mkdir dir");

        Assert.Equal(0, harness.Run("mv a.txt b.txt dir").ExitCode);
        Assert.Equal("first", File.ReadAllText(Path.Combine(harness.Root, "dir", "a.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(harness.Root, "dir", "b.txt")));
    }

    [Fact]
    public void MvMovesADirectory()
    {
        using ShellHarness harness = new();
        harness.Write("dir/f.txt", "body");

        Assert.Equal(0, harness.Run("mv dir moved").ExitCode);
        Assert.Equal("body", File.ReadAllText(Path.Combine(harness.Root, "moved", "f.txt")));
    }

    [Fact]
    public void MvOverwritesAnExistingTarget()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "new");
        harness.Write("b.txt", "old");

        Assert.Equal(0, harness.Run("mv a.txt b.txt").ExitCode);
        Assert.Equal("new", File.ReadAllText(Path.Combine(harness.Root, "b.txt")));
    }

    [Fact]
    public void MvWithoutTwoOperandsReportsItsUsage()
    {
        using ShellHarness harness = new();
        ShellResult result = harness.Run("mv only.txt");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("mv: usage: mv source... destination\n", result.Stderr);
    }

    [Fact]
    public void MvReportsAMissingSourceAndKeepsGoing()
    {
        using ShellHarness harness = new();
        harness.Write("present.txt", "body");
        harness.Run("mkdir dir");

        ShellResult result = harness.Run("mv absent.txt present.txt dir");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("mv: absent.txt: No such file or directory\n", result.Stderr);
        Assert.Equal("body", File.ReadAllText(Path.Combine(harness.Root, "dir", "present.txt")));
    }

    [Fact]
    public void MvRejectsAFlagItDoesNotImplement()
    {
        Assert.False(new MvApplet().CheckFlags(["-v", "a.txt", "b.txt"]).IsSupported);
        Assert.True(new MvApplet().CheckFlags(["-f", "a.txt", "b.txt"]).IsSupported);
        Assert.True(new MvApplet().CheckFlags(["-n", "a.txt", "b.txt"]).IsSupported);
    }

    [Fact]
    public void CpCopiesAFile()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "body");

        harness.Run("cp a.txt b.txt");

        Assert.Equal("body", File.ReadAllText(Path.Combine(harness.Root, "a.txt")));
        Assert.Equal("body", File.ReadAllText(Path.Combine(harness.Root, "b.txt")));
    }

    [Fact]
    public void CpNeedsDashRForDirectories()
    {
        using ShellHarness harness = new();
        harness.Write("dir/f.txt", "body");

        Assert.NotEqual(0, harness.Run("cp dir copy").ExitCode);
        Assert.Equal(0, harness.Run("cp -r dir copy").ExitCode);
        Assert.Equal("body", File.ReadAllText(Path.Combine(harness.Root, "copy", "f.txt")));
    }

    [Fact]
    public void TouchCreatesAnEmptyFile()
    {
        using ShellHarness harness = new();

        harness.Run("touch new.txt");

        Assert.Equal(string.Empty, File.ReadAllText(Path.Combine(harness.Root, "new.txt")));
    }

    [Theory]
    [InlineData("rm")]
    [InlineData("mv")]
    [InlineData("cp")]
    [InlineData("touch")]
    [InlineData("mkdir")]
    public void EveryMutationAppletRefusesToActOutsideTheWorkspace(string applet)
    {
        using ShellHarness harness = new();
        string outside = Path.Combine(Path.GetTempPath(), $"duetui-escape-{Guid.NewGuid():N}");

        ShellResult result = harness.Run($"{applet} {outside} {outside}.two");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(outside), "a mutation applet acted outside the workspace");
        Assert.False(Directory.Exists(outside), "a mutation applet acted outside the workspace");
    }

    [Fact]
    public void EveryMutationAppletDeclaresThatItMutates()
    {
        string[] mutating = ["mkdir", "rm", "mv", "cp", "touch"];

        Assert.All(
            AppletRegistry.CreateDefault().Applets.Where(applet => mutating.Contains(applet.Name)),
            applet => Assert.True(applet.Mutates, $"{applet.Name} does not declare that it mutates"));
    }

    [Fact]
    public void NoReadOnlyAppletClaimsToMutate()
    {
        string[] mutating = ["mkdir", "rm", "mv", "cp", "touch"];

        Assert.All(
            AppletRegistry.CreateDefault().Applets.Where(applet => !mutating.Contains(applet.Name)),
            applet => Assert.False(applet.Mutates, $"{applet.Name} wrongly declares that it mutates"));
    }
}
