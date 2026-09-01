using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

public class ExpansionTests
{
    private static string Out(string commandLine)
    {
        using ShellHarness harness = new();
        return harness.Run(commandLine).Stdout;
    }

    [Fact]
    public void ExpandsASetVariable()
    {
        Assert.Equal("1\n", Out("x=1; echo $x"));
    }

    [Fact]
    public void AnUnsetVariableExpandsToNothing()
    {
        Assert.Equal("\n", Out("echo $nope"));
    }

    [Fact]
    public void BracedFormIsTheSameVariable()
    {
        Assert.Equal("ab\n", Out("x=a; echo ${x}b"));
    }

    [Theory]
    [InlineData("echo ${y:-d}", "d\n")]
    [InlineData("y=set; echo ${y:-d}", "set\n")]
    [InlineData("y=; echo ${y:-d}", "d\n")]
    [InlineData("y=; echo ${y-d}", "\n")]
    [InlineData("echo ${y:+plus}", "\n")]
    [InlineData("y=v; echo ${y:+plus}", "plus\n")]
    [InlineData("echo ${y:=assigned}${y}", "assignedassigned\n")]
    public void HandlesTheDefaultingForms(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    [Fact]
    public void ReportsColonQuestionOnAnUnsetVariable()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("echo ${missing:?is required}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("is required", result.Stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("s=abcdef; echo ${#s}", "6\n")]
    [InlineData("s=a/b/c; echo ${s#*/}", "b/c\n")]
    [InlineData("s=a/b/c; echo ${s##*/}", "c\n")]
    [InlineData("s=a/b/c; echo ${s%/*}", "a/b\n")]
    [InlineData("s=a/b/c; echo ${s%%/*}", "a\n")]
    [InlineData("s=aXbXc; echo ${s/X/-}", "a-bXc\n")]
    [InlineData("s=aXbXc; echo ${s//X/-}", "a-b-c\n")]
    public void HandlesTheStringForms(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    [Fact]
    public void ExpandsTheLastExitCode()
    {
        Assert.Equal("1\n", Out("false; echo $?"));
    }

    [Theory]
    [InlineData("echo $((2 + 3 * 4))", "14\n")]
    [InlineData("echo $(( (2 + 3) * 4 ))", "20\n")]
    [InlineData("echo $((7 / 2))", "3\n")]
    [InlineData("echo $((7 % 2))", "1\n")]
    [InlineData("x=5; echo $((x + 1))", "6\n")]
    [InlineData("echo $((1 < 2))", "1\n")]
    [InlineData("echo $((1 && 0))", "0\n")]
    [InlineData("echo $((-3 + 1))", "-2\n")]
    public void EvaluatesArithmetic(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    [Fact]
    public void RunsCommandSubstitution()
    {
        Assert.Equal("hi\n", Out("echo $(echo hi)"));
    }

    [Fact]
    public void CommandSubstitutionStripsTrailingNewlines()
    {
        Assert.Equal("[hi]\n", Out("echo \"[$(echo hi)]\""));
    }

    [Fact]
    public void BackticksAreTheSameAsDollarParen()
    {
        Assert.Equal("hi\n", Out("echo `echo hi`"));
    }

    [Fact]
    public void UnquotedExpansionIsFieldSplit()
    {
        Assert.Equal("a b\n", Out("v=\"a  b\"; echo $v"));
    }

    [Fact]
    public void QuotedExpansionIsNotFieldSplit()
    {
        Assert.Equal("a  b\n", Out("v=\"a  b\"; echo \"$v\""));
    }

    [Fact]
    public void TildeExpandsToTheWorkspaceRoot()
    {
        using ShellHarness harness = new();

        Assert.Equal($"{harness.Root}\n", harness.Run("echo ~").Stdout);
    }

    [Fact]
    public void AnUnsupportedExpansionIsRefusedByName()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("x=a; echo ${x@Q}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("@Q", result.Stderr, StringComparison.Ordinal);
    }
}
