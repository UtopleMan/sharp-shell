using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

public class ExternalExecutorTests
{
    [Fact]
    public void TheSeamRunsARealProgramNatively()
    {
        Assert.SkipUnless(ProcessCommandExecutor.IsAvailable, "no platform shell available");

        using ShellHarness harness = new(new ProcessCommandExecutor());

        ShellResult result = harness.Run($"{ProcessCommandExecutor.ShellPath()} -c 'echo from-the-host'");

        Assert.Equal("from-the-host\n", result.Stdout);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void TheSpecCorpusHelpersAreImplementedInProcess()
    {
        using ShellHarness harness = new(new ProcessCommandExecutor());

        Assert.Equal("['a', 'b']\n", harness.Run("argv.py a b").Stdout);
    }

    [Fact]
    public void TheGuestsExecutorRefusesEverything()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("git status");

        Assert.Equal(127, result.ExitCode);
        Assert.Contains("command not found", result.Stderr, StringComparison.Ordinal);
    }
}
