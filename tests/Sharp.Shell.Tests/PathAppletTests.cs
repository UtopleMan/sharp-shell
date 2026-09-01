using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

public class PathAppletTests
{
    private static string Out(string commandLine)
    {
        using ShellHarness harness = new();
        return harness.Run(commandLine).Stdout;
    }

    [Fact]
    public void BasenameStripsTheDirectory()
    {
        Assert.Equal("file.txt\n", Out("basename /a/b/file.txt"));
    }

    [Fact]
    public void BasenameStripsASuffix()
    {
        Assert.Equal("file\n", Out("basename /a/b/file.txt .txt"));
    }

    [Fact]
    public void DirnameKeepsTheDirectory()
    {
        Assert.Equal("/a/b\n", Out("dirname /a/b/file.txt"));
    }

    [Fact]
    public void DirnameOfABareNameIsDot()
    {
        Assert.Equal(".\n", Out("dirname file.txt"));
    }

    [Fact]
    public void RealpathResolvesInsideTheWorkspace()
    {
        using ShellHarness harness = new();
        harness.Write("sub/f.txt", "");

        Assert.Equal($"{Path.Combine(harness.Root, "sub", "f.txt")}\n", harness.Run("realpath sub/f.txt").Stdout);
    }

    [Fact]
    public void RealpathRefusesToLeaveTheWorkspace()
    {
        using ShellHarness harness = new();

        Assert.NotEqual(0, harness.Run("realpath ../..").ExitCode);
    }

    [Fact]
    public void CommandDashVReportsAnOwnedBuiltin()
    {
        Assert.Equal("ls\n", Out("command -v ls"));
    }

    [Fact]
    public void CommandDashVFailsForAnythingNotOwned()
    {
        using ShellHarness harness = new();

        Assert.Equal(1, harness.Run("command -v git").ExitCode);
    }
}
