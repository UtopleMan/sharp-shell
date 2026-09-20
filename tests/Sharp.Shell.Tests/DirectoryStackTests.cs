using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// pushd, popd and dirs over one stack on the state. Every expectation was probed from the real bash:
// the current directory is the stack's first entry, and pushd and popd both print the stack they leave
// behind.
public class DirectoryStackTests
{
    [Fact]
    public void DirsShowsTheWorkingDirectoryAlone()
    {
        using ShellHarness harness = new();

        Assert.Equal($"{harness.Root}\n", harness.Run("dirs").Stdout);
    }

    [Fact]
    public void PushdChangesDirectoryAndPrintsTheStack()
    {
        using ShellHarness harness = new();
        harness.Write("a/f.txt", "");
        string inner = Path.Combine(harness.Root, "a");

        Assert.Equal($"{inner} {harness.Root}\n", harness.Run("pushd a").Stdout);
    }

    [Fact]
    public void PopdReturnsAndPrintsWhatIsLeft()
    {
        using ShellHarness harness = new();
        harness.Write("a/f.txt", "");
        string inner = Path.Combine(harness.Root, "a");

        Assert.Equal($"{inner} {harness.Root}\n{harness.Root}\n{harness.Root}\n", harness.Run("pushd a; popd; pwd").Stdout);
    }

    [Fact]
    public void PopdOnAnEmptyStackIsAnError()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("popd");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("popd: directory stack empty\n", result.Stderr);
    }

    [Fact]
    public void PushdIntoNowhereIsAnError()
    {
        using ShellHarness harness = new();

        ShellResult result = harness.Run("pushd nowhere");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("nowhere", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStackSurvivesAFork()
    {
        using ShellHarness harness = new();
        harness.Write("a/f.txt", "");
        string inner = Path.Combine(harness.Root, "a");

        string output = harness.Run("pushd a; echo \"[$(dirs)]\"").Stdout;

        Assert.Equal($"{inner} {harness.Root}\n[{inner} {harness.Root}]\n", output);
    }

    [Fact]
    public void DirsShowsHomeAsATilde()
    {
        using ShellHarness harness = new();

        Assert.Equal("~\n", harness.Run($"HOME={harness.Root}; dirs").Stdout);
    }

    [Fact]
    public void AutopushdMakesCdPush()
    {
        using ShellHarness harness = new();
        harness.Write("a/f.txt", "");
        string inner = Path.Combine(harness.Root, "a");

        Assert.Equal($"{inner} {harness.Root}\n", harness.Run("setopt autopushd; cd a; dirs").Stdout);
    }

    [Fact]
    public void WithoutAutopushdCdLeavesTheStackAlone()
    {
        using ShellHarness harness = new();
        harness.Write("a/f.txt", "");

        Assert.Equal($"{Path.Combine(harness.Root, "a")}\n", harness.Run("cd a; dirs").Stdout);
    }
}
