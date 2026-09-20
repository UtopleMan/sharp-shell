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
    public void CdDashReturnsToThePreviousDirectoryAndPrintsIt()
    {
        using ShellHarness harness = new();
        harness.Write("sub/f.txt", "");

        Assert.Equal($"{harness.Root}\n{harness.Root}\n", harness.Run("cd sub; cd -; pwd").Stdout);
    }

    [Fact]
    public void CdDashWithNothingToReturnToIsAnError()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("cd -");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("OLDPWD not set", result.Stderr, StringComparison.Ordinal);
    }

    // bash prints the directory it landed in whenever the target came from CDPATH rather than from
    // the working directory, because the one you get is not the one you typed.
    [Fact]
    public void CdFindsARelativeTargetThroughCdPath()
    {
        using ShellHarness harness = new();
        harness.Write("base/target/f.txt", "");
        string target = Path.Combine(harness.Root, "base", "target");

        Assert.Equal($"{target}\n{target}\n", harness.Run($"CDPATH={Path.Combine(harness.Root, "base")}; cd target; pwd").Stdout);
    }

    // Verified against the real bash: CDPATH is searched first, so the target you get is the one on
    // the path and not the one in the working directory.
    [Fact]
    public void CdPrefersCdPathOverTheWorkingDirectory()
    {
        using ShellHarness harness = new();
        harness.Write("target/here.txt", "");
        harness.Write("base/target/elsewhere.txt", "");
        string onThePath = Path.Combine(harness.Root, "base", "target");

        string output = harness.Run($"CDPATH={Path.Combine(harness.Root, "base")}; cd target; pwd").Stdout;

        Assert.Equal($"{onThePath}\n{onThePath}\n", output);
    }

    [Fact]
    public void CdIgnoresCdPathForADotRelativeTarget()
    {
        using ShellHarness harness = new();
        harness.Write("base/target/f.txt", "");

        ShellResult result = harness.Run($"CDPATH={Path.Combine(harness.Root, "base")}; cd ./target");

        Assert.NotEqual(0, result.ExitCode);
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

    // $$ and $! were refused by name because they name processes. They are answered now, with the
    // honesty that matters here: $$ is a fixed synthetic value, not a process id.
    [Fact]
    public void TheProcessIdIsSyntheticAndStable()
    {
        using ShellHarness harness = new();

        Assert.Equal("1 1\n", harness.Run("echo $$ $$").Stdout);
    }

    [Fact]
    public void TheLastBackgroundJobIsEmptyBecauseThereIsNeverOne()
    {
        using ShellHarness harness = new();

        Assert.Equal("[]\n", harness.Run("echo \"[$!]\"").Stdout);
    }

    [Fact]
    public void ThereAreNoPositionalParametersOutsideAFunction()
    {
        using ShellHarness harness = new();

        Assert.Equal("0 []\n", harness.Run("echo $# \"[$1]\"").Stdout);
    }

    [Fact]
    public void ArithmeticReadsThePositionalParameters()
    {
        using ShellHarness harness = new();

        Assert.Equal("5\n", harness.Run("add() { echo $(($1 + $2)); }; add 2 3").Stdout);
    }

    // "$@" is one string here and N fields in bash. They agree for arguments without whitespace,
    // and the case where they would not is refused rather than answered wrongly.
    [Fact]
    public void AllArgumentsJoinWhenNoneContainsWhitespace()
    {
        using ShellHarness harness = new();

        Assert.Equal("x y z\n", harness.Run("all() { echo $@; }; all x y z").Stdout);
    }

    [Fact]
    public void AllArgumentsIsRefusedWhenOneContainsWhitespace()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("all() { echo $@; }; all 'a b' c");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("$@", result.Stderr, StringComparison.Ordinal);
    }
}
