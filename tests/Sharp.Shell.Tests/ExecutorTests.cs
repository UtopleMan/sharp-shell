using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

public class ExecutorTests
{
    [Fact]
    public void RunsASimpleCommand()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("echo hi");

        Assert.Equal("hi\n", result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void AndShortCircuitsOnFailure()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("false && echo x");

        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void OrRunsTheRightSideOnFailure()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("false || echo x");

        Assert.Equal("x\n", result.Stdout);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void BangInvertsTheExitCode()
    {
        using ShellHarness harness = new();

        Assert.Equal(1, harness.Run("! true").ExitCode);
        Assert.Equal(0, harness.Run("! false").ExitCode);
    }

    [Fact]
    public void PipesOutputBetweenStages()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("echo a | wc -l");

        Assert.Equal("1\n", result.Stdout);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void APipelineTakesTheLastStagesExitCode()
    {
        using ShellHarness harness = new();

        Assert.Equal(0, harness.Run("false | true").ExitCode);
        Assert.Equal(1, harness.Run("true | false").ExitCode);
    }

    [Fact]
    public void SequencesRunInOrderAndKeepTheLastExitCode()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("echo a; echo b; false");

        Assert.Equal("a\nb\n", result.Stdout);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void PrintfEmitsNoTrailingNewlineOfItsOwn()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("printf '%s' x");

        Assert.Equal("x", result.Stdout);
    }

    [Fact]
    public void AnUnknownCommandIsExitCode127()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("definitely-not-a-command");

        Assert.Equal(127, result.ExitCode);
        Assert.Contains("command not found", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnsupportedConstructIsReportedNotEmulated()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("sleep 1 &");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("background", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExternalExecutorHandlesWhatTheRegistryDoesNotOwn()
    {
        using ShellHarness harness = new(new StubCommandExecutor("from the host\n", 0));

        ShellResult result = harness.Run("git status");

        Assert.Equal("from the host\n", result.Stdout);
        Assert.Equal(0, result.ExitCode);
    }
}
