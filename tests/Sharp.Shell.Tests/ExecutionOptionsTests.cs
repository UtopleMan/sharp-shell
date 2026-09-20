using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// errexit, nounset and pipefail change what a line does, so they live with the executor's unwind
// machinery rather than beside it. Every expectation here was probed from the real bash first.
public class ExecutionOptionsTests
{
    private static ShellResult Result(string commandLine)
    {
        using ShellHarness harness = new();
        return harness.Run(commandLine);
    }

    [Fact]
    public void ErrexitStopsASequenceAtTheFirstFailure()
    {
        ShellResult result = Result("set -e; false; echo never");

        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(1, result.ExitCode);
    }

    // The left side of && or || is being tested rather than trusted, so its failure is not the kind
    // errexit is about.
    [Fact]
    public void ErrexitExemptsTheLeftSideOfAnAndList()
    {
        ShellResult result = Result("set -e; false && echo x; echo after");

        Assert.Equal("after\n", result.Stdout);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ErrexitExemptsAnIfCondition()
    {
        ShellResult result = Result("set -e; if false; then echo t; fi; echo after");

        Assert.Equal("after\n", result.Stdout);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ErrexitExemptsAWhileCondition()
    {
        ShellResult result = Result("set -e; while false; do echo body; done; echo after");

        Assert.Equal("after\n", result.Stdout);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ErrexitExemptsANegatedCommand()
    {
        ShellResult result = Result("set -e; ! false; echo after");

        Assert.Equal("after\n", result.Stdout);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void WithoutErrexitAFailureIsJustAStatus()
    {
        Assert.Equal("after\n", Result("false; echo after").Stdout);
    }

    [Fact]
    public void NounsetFailsOnAnUnsetVariable()
    {
        ShellResult result = Result("set -u; echo $nope; echo never");

        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(127, result.ExitCode);
        Assert.Equal("test-shell: nope: unbound variable\n", result.Stderr);
    }

    // Asking whether a variable is set is the one thing nounset must not break.
    [Fact]
    public void NounsetLeavesTheDefaultingFormsAlone()
    {
        Assert.Equal("fallback\n", Result("set -u; echo ${nope-fallback}").Stdout);
    }

    [Fact]
    public void PipefailReportsTheRightmostFailingStage()
    {
        Assert.Equal("1\n", Result("set -o pipefail; false | true; echo $?").Stdout);
    }

    [Fact]
    public void WithoutPipefailOnlyTheLastStageCounts()
    {
        Assert.Equal("0\n", Result("false | true; echo $?").Stdout);
    }

    [Fact]
    public void PipefailKeepsTheLastStagesStatusWhenItFailed()
    {
        Assert.Equal("1\n", Result("set -o pipefail; true | false; echo $?").Stdout);
    }
}
