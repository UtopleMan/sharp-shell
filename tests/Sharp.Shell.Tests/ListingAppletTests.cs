using Sharp.Shell.Commands;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

public class ListingAppletTests
{
    [Fact]
    public void LsListsSortedEntriesOnePerLine()
    {
        using ShellHarness harness = new();
        harness.Write("b.txt", "");
        harness.Write("a.txt", "");

        Assert.Equal("a.txt\nb.txt\n", harness.Run("ls").Stdout);
    }

    [Fact]
    public void LsHidesDotFilesUnlessAsked()
    {
        using ShellHarness harness = new();
        harness.Write(".hidden", "");
        harness.Write("shown", "");

        Assert.Equal("shown\n", harness.Run("ls").Stdout);
        Assert.Equal(". .. .hidden shown\n", harness.Run("ls -a").Stdout.Replace('\n', ' ').TrimEnd() + "\n");
    }

    [Fact]
    public void LsListsAGivenDirectory()
    {
        using ShellHarness harness = new();
        harness.Write("sub/inner.txt", "");

        Assert.Equal("inner.txt\n", harness.Run("ls sub").Stdout);
    }

    [Fact]
    public void LsRecursesWithDashRInDirectoryBlocks()
    {
        using ShellHarness harness = new();
        harness.Write("sub/inner.txt", "");

        string output = harness.Run("ls -R").Stdout.Replace(Path.DirectorySeparatorChar, '/');

        Assert.Equal(".:\nsub\n\n./sub:\ninner.txt\n", output);
    }

    [Fact]
    public void LsRefusesToLeaveTheWorkspace()
    {
        using ShellHarness harness = new();

        Assert.NotEqual(0, harness.Run("ls ../..").ExitCode);
    }

    [Fact]
    public void LsRejectsLongFormat()
    {
        IApplet ls = AppletRegistry.CreateDefault().Applets.Single(applet => applet.Name == "ls");

        Assert.False(ls.CheckFlags(["-l"]).IsSupported);
    }

    [Fact]
    public void TailTakesTheLastLines()
    {
        using ShellHarness harness = new();
        harness.Write("f.txt", "1\n2\n3\n4\n5\n");

        Assert.Equal("4\n5\n", harness.Run("tail -n 2 f.txt").Stdout);
    }

    [Fact]
    public void TailDefaultsToTenLines()
    {
        using ShellHarness harness = new();
        harness.Write("f.txt", string.Concat(Enumerable.Range(1, 20).Select(number => $"{number}\n")));

        Assert.Equal(10, harness.Run("tail f.txt").Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void TailReadsItsInputWhenGivenNoFile()
    {
        using ShellHarness harness = new();

        Assert.Equal("b\n", harness.Run("printf 'a\\nb\\n' | tail -n 1").Stdout);
    }

    [Fact]
    public void TailRejectsFollow()
    {
        IApplet tail = AppletRegistry.CreateDefault().Applets.Single(applet => applet.Name == "tail");

        Assert.False(tail.CheckFlags(["-f"]).IsSupported);
    }
}
