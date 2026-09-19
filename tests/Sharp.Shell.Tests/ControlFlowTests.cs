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

    // Functions, select and [[ ]] were refused by name until the shell stopped handing whole lines
    // away. None of them needs a process; they were simply unwritten.

    [Fact]
    public void AFunctionRunsItsBodyWhenItIsCalled()
    {
        Assert.Equal("hi\n", Out("greet() { echo hi; }; greet"));
    }

    [Fact]
    public void TheKeywordFormDefinesTheSameFunction()
    {
        Assert.Equal("hi\n", Out("function greet { echo hi; }; greet"));
        Assert.Equal("hi\n", Out("function greet() { echo hi; }; greet"));
    }

    [Fact]
    public void AFunctionReceivesItsArgumentsAsPositionalParameters()
    {
        Assert.Equal("b 3\n", Out("show() { echo $2 $#; }; show a b c"));
    }

    [Fact]
    public void ThePositionalParametersAreRestoredAfterTheCall()
    {
        Assert.Equal("inner\n\n", Out("show() { echo $1; }; show inner; echo $1"));
    }

    [Fact]
    public void AFunctionSeesTheCallersWorkingDirectoryAndChangesItForGood()
    {
        using ShellHarness harness = new();
        Directory.CreateDirectory(Path.Combine(harness.Root, "inner"));

        ShellResult result = harness.Run("move() { cd inner; }; move; pwd");

        Assert.Equal($"{Path.Combine(harness.Root, "inner")}\n", result.Stdout);
    }

    [Fact]
    public void AFunctionCallsAnotherFunction()
    {
        Assert.Equal("nested\n", Out("outer() { inner; }; inner() { echo nested; }; outer"));
    }

    [Fact]
    public void AFunctionIsAPipelineStage()
    {
        Assert.Equal("HI\n", Out("greet() { echo hi; }; greet | tr a-z A-Z"));
    }

    [Fact]
    public void AFunctionReadsItsStandardInput()
    {
        Assert.Equal("piped\n", Out("through() { cat; }; echo piped | through"));
    }

    [Fact]
    public void AFunctionTakesPrecedenceOverAnAppletOfTheSameName()
    {
        Assert.Equal("shadowed\n", Out("echo() { printf 'shadowed\\n'; }; echo hi"));
    }

    // A stack overflow cannot be caught, so runaway recursion is stopped by a cap rather than by
    // the process dying.
    [Fact]
    public void RunawayRecursionIsStoppedRatherThanCrashing()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("loop() { loop; }; loop");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("nesting exceeded", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectRunsItsBodyOncePerReply()
    {
        Assert.Equal("alpha\nbeta\n\n", Out("printf '1\\n2\\n' | select x in alpha beta; do echo $x; done"));
    }

    [Fact]
    public void SelectWritesItsMenuToStandardError()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("printf '1\\n' | select x in alpha beta; do echo $x; done");

        Assert.Equal("1) alpha\n2) beta\n", result.Stderr);
    }

    [Fact]
    public void SelectLeavesTheVariableEmptyForAReplyOutOfRange()
    {
        Assert.Equal("[]\n\n", Out("printf '9\\n' | select x in alpha beta; do echo \"[$x]\"; done"));
    }

    [Fact]
    public void SelectRecordsTheRawReply()
    {
        Assert.Equal("9\n\n", Out("printf '9\\n' | select x in alpha; do echo $REPLY; done"));
    }

    [Fact]
    public void ADoubleBracketConditionAnswersLikeTest()
    {
        Assert.Equal("0\n", Out("[[ -n x ]]; echo $?"));
        Assert.Equal("1\n", Out("[[ -z x ]]; echo $?"));
        Assert.Equal("0\n", Out("[[ 2 -lt 3 ]]; echo $?"));
    }

    // The two places [[ ]] is not test: == matches a pattern, and the operands are not split.
    [Fact]
    public void DoubleBracketEqualsMatchesAPattern()
    {
        Assert.Equal("0\n", Out("f=notes.cs; [[ $f == *.cs ]]; echo $?"));
        Assert.Equal("1\n", Out("f=notes.md; [[ $f == *.cs ]]; echo $?"));
    }

    [Fact]
    public void DoubleBracketDoesNotSplitAnUnquotedValue()
    {
        Assert.Equal("0\n", Out("v='a b'; [[ -n $v ]]; echo $?"));
        Assert.Equal("0\n", Out("v='a b'; [[ $v == 'a b' ]]; echo $?"));
    }

    [Fact]
    public void DoubleBracketDoesNotGlobItsOperands()
    {
        using ShellHarness harness = new();
        harness.Write("one.cs", string.Empty);
        harness.Write("two.cs", string.Empty);

        Assert.Equal("0\n", harness.Run("[[ '*.cs' == '*.cs' ]]; echo $?").Stdout);
    }

    [Fact]
    public void DoubleBracketTakesRegularExpressions()
    {
        Assert.Equal("0\n", Out("[[ abc =~ ^a.c$ ]]; echo $?"));
        Assert.Equal("1\n", Out("[[ abc =~ ^b ]]; echo $?"));
    }

    [Fact]
    public void DoubleBracketJoinsConditionsWithTheShellOperators()
    {
        Assert.Equal("0\n", Out("[[ a == a && b == b ]]; echo $?"));
        Assert.Equal("1\n", Out("[[ a == a && b == z ]]; echo $?"));
        Assert.Equal("0\n", Out("[[ a == z || b == b ]]; echo $?"));
        Assert.Equal("0\n", Out("[[ ( -n x ) ]]; echo $?"));
    }

    // `]]` only closes the condition when it is bare; quoted, it is an operand like any other.
    [Fact]
    public void AQuotedTerminatorIsAnOperand()
    {
        Assert.Equal("1\n", Out("[[ -z ']]' ]]; echo $?"));
    }
}
