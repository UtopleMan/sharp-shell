using Sharp.Shell.Commands;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

public class AppletTests
{
    [Fact]
    public void EchoJoinsItsArgumentsWithSpaces()
    {
        using ShellHarness harness = new();

        Assert.Equal("a b c\n", harness.Run("echo a b c").Stdout);
    }

    [Fact]
    public void EchoMinusNSuppressesTheNewline()
    {
        using ShellHarness harness = new();

        Assert.Equal("a", harness.Run("echo -n a").Stdout);
    }

    [Fact]
    public void CatStreamsItsOperands()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "one\n");
        harness.Write("b.txt", "two\n");

        Assert.Equal("one\ntwo\n", harness.Run("cat a.txt b.txt").Stdout);
    }

    [Fact]
    public void CatWithNoOperandsCopiesItsInput()
    {
        using ShellHarness harness = new();

        Assert.Equal("hi\n", harness.Run("echo hi | cat").Stdout);
    }

    [Fact]
    public void CatReportsAMissingFileAndKeepsGoing()
    {
        using ShellHarness harness = new();
        harness.Write("b.txt", "two\n");

        Sharp.Shell.Execution.ShellResult result = harness.Run("cat missing.txt b.txt");

        Assert.Equal("two\n", result.Stdout);
        Assert.Contains("missing.txt", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void CatRefusesToLeaveTheWorkspace()
    {
        using ShellHarness harness = new();

        Sharp.Shell.Execution.ShellResult result = harness.Run("cat ../../../etc/passwd");

        Assert.Equal(string.Empty, result.Stdout);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Theory]
    [InlineData("wc -l", "2\n")]
    [InlineData("wc -w", "3\n")]
    [InlineData("wc -c", "14\n")]
    public void WcCountsWhatItIsAsked(string command, string expected)
    {
        using ShellHarness harness = new();
        harness.Write("f.txt", "one two\nthree\n");

        Assert.Equal(expected, harness.Run($"cat f.txt | {command}").Stdout);
    }

    [Fact]
    public void HeadDefaultsToTenLines()
    {
        using ShellHarness harness = new();
        harness.Write("f.txt", string.Concat(Enumerable.Range(1, 20).Select(number => $"{number}\n")));

        Assert.Equal(10, harness.Run("cat f.txt | head").Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void TrueAndFalseReportTheirStatus()
    {
        using ShellHarness harness = new();

        Assert.Equal(0, harness.Run("true").ExitCode);
        Assert.Equal(1, harness.Run("false").ExitCode);
    }

    [Fact]
    public void PrintfHandlesTheConversionsItSupports()
    {
        using ShellHarness harness = new();

        Assert.Equal("a-1%\n", harness.Run("printf '%s-%d%%\\n' a 1").Stdout);
    }

    [Fact]
    public void PrintfRejectsAConversionItDoesNotImplement()
    {
        IApplet printf = AppletRegistry.CreateDefault().Applets.Single(applet => applet.Name == "printf");

        FlagSupport support = printf.CheckFlags(["%q"]);

        Assert.False(support.IsSupported);
        Assert.Equal("%q", support.UnsupportedFlag);
    }

    [Fact]
    public void HeadRejectsAFlagItDoesNotImplement()
    {
        IApplet head = AppletRegistry.CreateDefault().Applets.Single(applet => applet.Name == "head");

        Assert.False(head.CheckFlags(["-c", "5"]).IsSupported);
    }

}
