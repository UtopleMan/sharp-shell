using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

public class ShellBuiltinTests
{
    [Fact]
    public void PwdReportsTheWorkingDirectory()
    {
        using ShellHarness harness = new();

        Assert.Equal($"{harness.Root}\n", harness.Run("pwd").Stdout);
    }

    [Fact]
    public void CdMovesAndPwdFollows()
    {
        using ShellHarness harness = new();
        harness.Write("sub/f.txt", "");

        Assert.Equal($"{Path.Combine(harness.Root, "sub")}\n", harness.Run("cd sub; pwd").Stdout);
    }

    [Fact]
    public void CdRefusesToLeaveTheWorkspace()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("cd ..");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("outside the workspace", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportMakesAVariableAvailable()
    {
        using ShellHarness harness = new();

        Assert.Equal("v\n", harness.Run("export x=v; echo $x").Stdout);
    }

    // Deviation from bash, recorded deliberately: bash runs each pipeline stage in a subshell, so a
    // variable set by `read` there is invisible afterwards. This shell has no subshells to fork, so
    // the assignment survives. Every command is a function call in one address space — that is the
    // whole design, and this is where it shows.
    [Fact]
    public void ReadTakesOneLineFromTheInput()
    {
        using ShellHarness harness = new();

        Assert.Equal("hello\n", harness.Run("echo hello | read line; echo $line").Stdout);
    }

    [Fact]
    public void ExitStopsTheProgramWithItsStatus()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("echo a; exit 3; echo b");

        Assert.Equal("a\n", result.Stdout);
        Assert.Equal(3, result.ExitCode);
    }

    [Theory]
    [InlineData("[ -z '' ]", 0)]
    [InlineData("[ -n '' ]", 1)]
    [InlineData("[ a = a ]", 0)]
    [InlineData("[ a != a ]", 1)]
    [InlineData("[ 2 -eq 2 ]", 0)]
    [InlineData("[ 2 -lt 1 ]", 1)]
    [InlineData("[ 2 -ge 2 ]", 0)]
    [InlineData("test 1 -ne 2", 0)]
    [InlineData("[ ! -z x ]", 0)]
    public void TestEvaluatesItsExpression(string commandLine, int expected)
    {
        using ShellHarness harness = new();

        Assert.Equal(expected, harness.Run(commandLine).ExitCode);
    }

    [Fact]
    public void TestChecksTheFilesystem()
    {
        using ShellHarness harness = new();
        harness.Write("f.txt", "x");

        Assert.Equal(0, harness.Run("[ -f f.txt ]").ExitCode);
        Assert.Equal(0, harness.Run("[ -e f.txt ]").ExitCode);
        Assert.Equal(0, harness.Run("[ -s f.txt ]").ExitCode);
        Assert.Equal(1, harness.Run("[ -d f.txt ]").ExitCode);
        Assert.Equal(1, harness.Run("[ -f missing ]").ExitCode);
    }

    [Fact]
    public void TestRejectsAnOperatorItDoesNotImplement()
    {
        IApplet test = AppletRegistry.CreateDefault().Applets.Single(applet => applet.Name == "test");

        Assert.False(test.CheckFlags(["-N", "f"]).IsSupported);
    }

    [Fact]
    public void GuardsAgainstAnUnclosedBracket()
    {
        using ShellHarness harness = new();

        Assert.NotEqual(0, harness.Run("[ a = a").ExitCode);
    }
}
