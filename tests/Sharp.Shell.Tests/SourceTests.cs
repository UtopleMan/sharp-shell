using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// source runs a file against the *current* state, which is the whole point: a cd or an export inside
// it has to stick. It is what makes an rc file possible, and the rc loading itself is the binary's job.
public class SourceTests
{
    [Fact]
    public void ASourcedFileRunsItsCommands()
    {
        using ShellHarness harness = new();
        harness.Write("start.sh", "echo from-the-file\n");

        Assert.Equal("from-the-file\n", harness.Run("source start.sh").Stdout);
    }

    [Fact]
    public void ADotIsTheSameBuiltin()
    {
        using ShellHarness harness = new();
        harness.Write("start.sh", "echo from-the-file\n");

        Assert.Equal("from-the-file\n", harness.Run(". start.sh").Stdout);
    }

    [Fact]
    public void AnExportInsideASourcedFilePersists()
    {
        using ShellHarness harness = new();
        harness.Write("start.sh", "export MARKER=set\n");

        Assert.Equal("set\n", harness.Run("source start.sh; echo $MARKER").Stdout);
    }

    [Fact]
    public void ACdInsideASourcedFilePersists()
    {
        using ShellHarness harness = new();
        harness.Write("sub/f.txt", "");
        harness.Write("start.sh", "cd sub\n");

        Assert.Equal($"{Path.Combine(harness.Root, "sub")}\n", harness.Run("source start.sh; pwd").Stdout);
    }

    [Fact]
    public void AnAliasDefinedInASourcedFileIsUsable()
    {
        using ShellHarness harness = new();
        harness.Write("start.sh", "alias greet='echo hello'\n");

        Assert.Equal("hello\n", harness.Run("source start.sh; greet").Stdout);
    }

    // A sourced file reads the stdin the command was given, as bash does. Without it the pipe stops at
    // the builtin and the file sees nothing.
    [Fact]
    public void ASourcedFileReadsTheInputItWasPiped()
    {
        using ShellHarness harness = new();
        harness.Write("start.sh", "cat\n");

        Assert.Equal("hi\n", harness.Run("echo hi | source start.sh").Stdout);
    }

    // Deviation from bash, recorded deliberately and shared with ShellExecutor.Run's own input
    // overload: the stdin reaches a body that is *one* command. RunSequence hands TextStream.Empty to
    // each item it runs, so the second line of a sourced file cannot read what the first did not take.
    // bash gives every command in the file the same stdin.
    [Fact]
    public void ASequenceInASourcedFileDoesNotReadThatInputYet()
    {
        using ShellHarness harness = new();
        harness.Write("start.sh", "read line\necho \"[$line]\"\n");

        Assert.Equal("[]\n", harness.Run("echo hi | source start.sh").Stdout);
    }

    [Fact]
    public void AFileThatCannotBeReadIsAnErrorWithItsReason()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("source nowhere.sh");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("nowhere.sh", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("No such file or directory", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceWithNoOperandIsAnError()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("source");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("filename argument required", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStatusOfASourcedFileIsItsLastCommands()
    {
        using ShellHarness harness = new();
        harness.Write("start.sh", "true\nfalse\n");

        Assert.Equal(1, harness.Run("source start.sh").ExitCode);
    }

    // A sourced exit ends the run that sourced it, not just the file.
    [Fact]
    public void AnExitInsideASourcedFileEndsTheRun()
    {
        using ShellHarness harness = new();
        harness.Write("start.sh", "exit 7\n");

        ShellResult result = harness.Run("source start.sh; echo never");

        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(7, result.ExitCode);
    }
}
