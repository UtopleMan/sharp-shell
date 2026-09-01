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

    // Every row checked against `bash --norc --noprofile -c '<line>' | od -c` first: the two builtins
    // read octal differently, and an escape neither defines keeps its backslash.
    [Theory]
    [InlineData("printf 'a\\vb\\n'", "a\vb\n")]
    [InlineData("printf 'a\\fb\\n'", "a\fb\n")]
    [InlineData("printf 'a\\eb\\n'", "a\u001bb\n")]
    [InlineData("printf 'a\\101b\\n'", "aAb\n")]
    [InlineData("printf 'a\\7b\\n'", "a\ab\n")]
    [InlineData("printf 'a\\0b\\n'", "a\0b\n")]
    [InlineData("printf 'a\\01b\\n'", "a\u0001b\n")]
    [InlineData("printf 'a\\010b\\n'", "a\bb\n")]
    [InlineData("printf 'a\\0101b\\n'", "a\b1b\n")]
    [InlineData("printf 'a\\x41b\\n'", "aAb\n")]
    [InlineData("printf 'a\\x4bb\\n'", "aKb\n")]
    [InlineData("printf 'a\\qb\\n'", "a\\qb\n")]
    [InlineData("printf 'a\\8b\\n'", "a\\8b\n")]
    public void PrintfReadsTheEscapesBashReads(string commandLine, string expected)
    {
        using ShellHarness harness = new();

        Assert.Equal(expected, harness.Run(commandLine).Stdout);
    }

    [Theory]
    [InlineData("echo -e 'a\\vb'", "a\vb\n")]
    [InlineData("echo -e 'a\\fb'", "a\fb\n")]
    [InlineData("echo -e 'a\\x41b'", "aAb\n")]
    [InlineData("echo -e 'a\\0101b'", "aAb\n")]
    [InlineData("echo -e 'a\\010b'", "a\bb\n")]
    [InlineData("echo -e 'a\\0b'", "a\0b\n")]
    [InlineData("echo -e 'a\\101b'", "a\\101b\n")]
    [InlineData("echo -e 'a\\qb'", "a\\qb\n")]
    [InlineData("echo 'a\\vb'", "a\\vb\n")]
    public void EchoReadsOctalTheWayBashsEchoDoes(string commandLine, string expected)
    {
        using ShellHarness harness = new();

        Assert.Equal(expected, harness.Run(commandLine).Stdout);
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
