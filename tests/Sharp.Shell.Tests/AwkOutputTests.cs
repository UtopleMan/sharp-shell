using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// Redirection is the one place an awk program touches the workspace, so every path it names goes
// through the same guard `rm` and `sed` use.
public class AwkOutputTests
{
    [Fact]
    public void PrintCanRedirectToAFile()
    {
        using ShellHarness harness = new();

        harness.Run("printf 'a\\nb\\n' | awk '{print > \"out.txt\"}'");

        Assert.Equal("a\nb\n", File.ReadAllText(Path.Combine(harness.Root, "out.txt")));
    }

    [Fact]
    public void AppendKeepsWhatWasThereBefore()
    {
        using ShellHarness harness = new();
        harness.Write("log.txt", "before\n");

        harness.Run("printf 'a\\n' | awk '{print >> \"log.txt\"}'");

        Assert.Equal("before\na\n", File.ReadAllText(Path.Combine(harness.Root, "log.txt")));
    }

    // sed creates its `w` files when the script loads; awk does not open a target until something is
    // written to it, which both reference implementations agree on.
    [Fact]
    public void ATargetThatIsNeverWrittenToIsLeftAlone()
    {
        using ShellHarness harness = new();
        harness.Write("out.txt", "stale\n");

        harness.Run("printf 'a\\n' | awk '/never matches/ {print > \"out.txt\"}'");

        Assert.Equal("stale\n", File.ReadAllText(Path.Combine(harness.Root, "out.txt")));
    }

    [Fact]
    public void TheFirstWriteTruncatesWhatWasThereBefore()
    {
        using ShellHarness harness = new();
        harness.Write("out.txt", "stale\n");

        harness.Run("printf 'a\\n' | awk '{print > \"out.txt\"}'");

        Assert.Equal("a\n", File.ReadAllText(Path.Combine(harness.Root, "out.txt")));
    }

    [Fact]
    public void SeparateTargetsGetSeparateFiles()
    {
        using ShellHarness harness = new();

        harness.Run("printf 'a\\nb\\n' | awk 'NR == 1 {print > \"one.txt\"} NR == 2 {print > \"two.txt\"}'");

        Assert.Equal("a\n", File.ReadAllText(Path.Combine(harness.Root, "one.txt")));
        Assert.Equal("b\n", File.ReadAllText(Path.Combine(harness.Root, "two.txt")));
    }

    [Fact]
    public void ADynamicTargetIsOpenedWhenItIsFirstUsed()
    {
        using ShellHarness harness = new();

        harness.Run("printf 'a\\nb\\n' | awk '{print > (\"file\" NR \".txt\")}'");

        Assert.Equal("a\n", File.ReadAllText(Path.Combine(harness.Root, "file1.txt")));
        Assert.Equal("b\n", File.ReadAllText(Path.Combine(harness.Root, "file2.txt")));
    }

    [Fact]
    public void DevStdoutIsTheAppletsOwnOutput()
    {
        Assert.Equal("a\n", Out("printf 'a\\n' | awk '{print > \"/dev/stdout\"}'"));
    }

    [Fact]
    public void DevStderrIsTheAppletsOwnErrorStream()
    {
        using ShellHarness harness = new();
        ShellResult result = harness.Run("printf 'a\\n' | awk '{print \"oops\" > \"/dev/stderr\"}'");

        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal("oops\n", result.Stderr);
    }

    [Fact]
    public void AWriteOutsideTheWorkspaceIsRefused()
    {
        using ShellHarness harness = new();
        ShellResult result = harness.Run("printf 'a\\n' | awk '{print > \"../escape.txt\"}'");

        Assert.Contains("awk: ../escape.txt: outside the workspace", result.Stderr, StringComparison.Ordinal);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(harness.Root, "..", "escape.txt")));
    }

    [Fact]
    public void CloseAnswersZeroForAnOpenStreamAndMinusOneOtherwise()
    {
        using ShellHarness harness = new();

        Assert.Equal(
            "0\n-1\n",
            harness.Run("printf 'a\\n' | awk '{print > \"out.txt\"; print close(\"out.txt\"); print close(\"never.txt\")}'").Stdout);
    }

    [Fact]
    public void ReopeningAClosedTargetTruncatesItAgain()
    {
        using ShellHarness harness = new();

        harness.Run("printf 'a\\nb\\n' | awk '{print > \"out.txt\"; close(\"out.txt\")}'");

        Assert.Equal("b\n", File.ReadAllText(Path.Combine(harness.Root, "out.txt")));
    }

    [Fact]
    public void FflushIsAcceptedAndChangesNothing()
    {
        Assert.Equal("a\n", Out("printf 'a\\n' | awk '{print; fflush()}'"));
    }

    [Fact]
    public void PrintfCanRedirectTheSameWay()
    {
        using ShellHarness harness = new();

        harness.Run("printf 'a\\n' | awk '{printf \"%s!\", $0 > \"out.txt\"}'");

        Assert.Equal("a!", File.ReadAllText(Path.Combine(harness.Root, "out.txt")));
    }

    private static string Out(string commandLine)
    {
        using ShellHarness harness = new();
        return harness.Run(commandLine).Stdout;
    }
}
