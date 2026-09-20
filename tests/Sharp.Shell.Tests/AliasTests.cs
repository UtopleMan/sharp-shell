using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// An alias is a substitution at the command position, not a variable and not a function. The rule
// that matters for a host: the approver is asked about the command the alias produced.
public class AliasTests
{
    private static string Out(string commandLine)
    {
        using ShellHarness harness = new();
        return harness.Run(commandLine).Stdout;
    }

    [Fact]
    public void AnAliasStandsInForACommand()
    {
        Assert.Equal("hello\n", Out("alias greet='echo hello'; greet"));
    }

    [Fact]
    public void AnAliasKeepsTheArgumentsThatFollowIt()
    {
        Assert.Equal("hello there\n", Out("alias greet='echo hello'; greet there"));
    }

    [Fact]
    public void AnAliasIsNotExpandedInAnArgumentPosition()
    {
        Assert.Equal("greet\n", Out("alias greet='echo hello'; echo greet"));
    }

    // bash's guard: the name is expanded once, so a body that names the alias reaches the command
    // instead of looping.
    [Fact]
    public void ASelfReferentialAliasTerminates()
    {
        using ShellHarness harness = new();
        harness.Write("f.txt", "");

        Assert.Equal("f.txt\n", harness.Run("alias ls='ls f.txt'; ls").Stdout);
    }

    [Fact]
    public void AliasWithNoOperandsListsWhatIsDefined()
    {
        Assert.Equal("alias greet='echo hello'\n", Out("alias greet='echo hello'; alias"));
    }

    [Fact]
    public void UnaliasRemovesIt()
    {
        Assert.Equal(string.Empty, Out("alias greet='echo hello'; unalias greet; alias"));
    }

    [Fact]
    public void UnaliasOfAnUnknownNameIsAnError()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("unalias nothing");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("unalias: nothing: not found\n", result.Stderr);
    }

    [Fact]
    public void AliasesSurviveAFork()
    {
        Assert.Equal("hello\n", Out("alias greet='echo hello'; echo $(greet)"));
    }

    // The host must not be asked about `ll` when `ls -la` is what runs.
    [Fact]
    public void TheApproverIsAskedAboutTheExpandedCommand()
    {
        RecordingCommandApprover approver = new();
        using ShellHarness harness = new(approver: approver);

        harness.Run("alias hi='echo hello'; hi");

        Assert.Contains("echo hello", approver.Texts);
        Assert.DoesNotContain("hi", approver.Texts);
    }
}
