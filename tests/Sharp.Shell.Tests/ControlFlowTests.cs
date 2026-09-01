using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

public class ControlFlowTests
{
    private static string Out(string commandLine)
    {
        using ShellHarness harness = new();
        return harness.Run(commandLine).Stdout;
    }

    [Fact]
    public void RunsTheThenBranch()
    {
        Assert.Equal("y\n", Out("if true; then echo y; fi"));
    }

    [Fact]
    public void SkipsTheThenBranchOnFailure()
    {
        Assert.Equal(string.Empty, Out("if false; then echo y; fi"));
    }

    [Fact]
    public void RunsTheElseBranch()
    {
        Assert.Equal("n\n", Out("if false; then echo y; else echo n; fi"));
    }

    [Fact]
    public void RunsAnElifBranch()
    {
        Assert.Equal("e\n", Out("if false; then echo y; elif true; then echo e; else echo n; fi"));
    }

    [Fact]
    public void AnIfWithNoMatchingBranchExitsZero()
    {
        using ShellHarness harness = new();

        Assert.Equal(0, harness.Run("if false; then echo y; fi").ExitCode);
    }

    [Fact]
    public void LoopsWhileTheConditionHolds()
    {
        Assert.Equal("3\n", Out("i=0; while [ $i -lt 3 ]; do i=$((i+1)); done; echo $i"));
    }

    [Fact]
    public void UntilLoopsWhileTheConditionFails()
    {
        Assert.Equal("3\n", Out("i=0; until [ $i -ge 3 ]; do i=$((i+1)); done; echo $i"));
    }

    [Fact]
    public void ForIteratesItsWords()
    {
        Assert.Equal("a\nb\nc\n", Out("for i in a b c; do echo $i; done"));
    }

    [Fact]
    public void ForIteratesAGlob()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "");
        harness.Write("b.txt", "");

        Assert.Equal("a.txt\nb.txt\n", harness.Run("for f in *.txt; do echo $f; done").Stdout);
    }

    [Fact]
    public void CaseMatchesTheFirstArm()
    {
        Assert.Equal("m\n", Out("case x in x) echo m;; *) echo n;; esac"));
    }

    [Fact]
    public void CaseFallsThroughToTheDefault()
    {
        Assert.Equal("n\n", Out("case zzz in x) echo m;; *) echo n;; esac"));
    }

    [Fact]
    public void CaseMatchesAnyOfSeveralPatterns()
    {
        Assert.Equal("m\n", Out("case b in a|b) echo m;; *) echo n;; esac"));
    }

    [Fact]
    public void ASubshellDoesNotLeakItsDirectory()
    {
        using ShellHarness harness = new();
        harness.Write("sub/f.txt", "");

        ShellResult result = harness.Run("(cd sub; pwd); pwd");

        Assert.Equal($"{Path.Combine(harness.Root, "sub")}\n{harness.Root}\n", result.Stdout);
    }

    [Fact]
    public void ASubshellDoesNotLeakItsVariables()
    {
        Assert.Equal("outer\n", Out("x=outer; (x=inner); echo $x"));
    }

    [Fact]
    public void ABraceGroupSharesTheOuterState()
    {
        Assert.Equal("inner\n", Out("x=outer; { x=inner; }; echo $x"));
    }

    [Fact]
    public void NestedControlFlowRuns()
    {
        Assert.Equal("a1\na2\n", Out("for i in 1 2; do if true; then echo a$i; fi; done"));
    }

    [Fact]
    public void AControlFlowBodyCanPipe()
    {
        Assert.Equal("1\n", Out("if true; then echo x | wc -l; fi"));
    }

    // Regression: a compound command as a non-final pipeline stage used to crash, because only
    // simple commands were expected there.
    [Theory]
    [InlineData("for i in 1 2 3; do echo $i; done | head -2", "1\n2\n")]
    [InlineData("if true; then echo a; echo b; fi | wc -l", "2\n")]
    [InlineData("{ echo x; echo y; } | head -1", "x\n")]
    public void ACompoundCommandCanBeAPipelineStage(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    [Fact]
    public void ExitInsideALoopStopsEverything()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("for i in 1 2 3; do echo $i; exit 7; done; echo after");

        Assert.Equal("1\n", result.Stdout);
        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public void AnUnterminatedIfIsAParseFailure()
    {
        using ShellHarness harness = new();

        Assert.NotEqual(0, harness.Run("if true; then echo y").ExitCode);
    }
}
