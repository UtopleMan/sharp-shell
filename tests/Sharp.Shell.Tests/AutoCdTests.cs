using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// autocd turns a bare directory name into a cd. It happens at the dispatch point rather than in the
// parser, so the command the approver is asked about is the cd that actually runs.
public class AutoCdTests
{
    [Fact]
    public void ADirectoryNameChangesDirectoryWhenAutocdIsOn()
    {
        using ShellHarness harness = new();
        harness.Write("sub/f.txt", "");

        Assert.Equal($"{Path.Combine(harness.Root, "sub")}\n", harness.Run("setopt autocd; sub; pwd").Stdout);
    }

    [Fact]
    public void WithoutAutocdADirectoryNameIsStillACommandNotFound()
    {
        using ShellHarness harness = new();
        harness.Write("sub/f.txt", "");

        Assert.Equal("test-shell: sub: command not found\n", harness.Run("sub").Stderr);
    }

    [Fact]
    public void ADirectoryNameWithArgumentsIsNotADirectoryChange()
    {
        using ShellHarness harness = new();
        harness.Write("sub/f.txt", "");

        Assert.Equal("test-shell: sub: command not found\n", harness.Run("setopt autocd; sub extra").Stderr);
    }

    [Fact]
    public void TheApproverIsAskedAboutTheCdThatRuns()
    {
        RecordingCommandApprover approver = new();
        using ShellHarness harness = new(approver: approver);
        harness.Write("sub/f.txt", "");

        harness.Run("setopt autocd; sub");

        Assert.Contains("cd sub", approver.Texts);
    }
}
