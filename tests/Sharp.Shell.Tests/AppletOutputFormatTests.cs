using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// Where the applets deliberately differ from the coreutils on some platforms, the format is pinned
// here rather than compared against whatever is installed.
//
// These cannot live in the bash differential corpus: BSD `wc` and `uniq -c` pad their counts into
// columns and the GNU ones do not, so a ratchet over them would pass on macOS and fail on Linux.
// The choice is GNU-style, unpadded, everywhere — a stable format the model can parse.
public class AppletOutputFormatTests
{
    [Fact]
    public void WcDoesNotPadItsCounts()
    {
        using ShellHarness harness = new();

        Assert.Equal("1\n", harness.Run("echo a | wc -l").Stdout);
    }

    [Fact]
    public void WcFromAFileDoesNotPadEither()
    {
        using ShellHarness harness = new();
        harness.Write("f.txt", "one\ntwo\n");

        Assert.Equal("2\n", harness.Run("wc -l < f.txt").Stdout);
    }

    [Fact]
    public void UniqCountsWithASingleSpace()
    {
        using ShellHarness harness = new();

        Assert.Equal("2 a\n1 b\n", harness.Run("printf 'a\\na\\nb\\n' | uniq -c").Stdout);
    }

    [Fact]
    public void WcWithNoFlagsReportsLinesWordsCharacters()
    {
        using ShellHarness harness = new();

        Assert.Equal("1 2 4\n", harness.Run("printf 'a b\\n' | wc").Stdout);
    }

    [Fact]
    public void WcNamesTheFileItCounted()
    {
        using ShellHarness harness = new();
        harness.Write("f.txt", "one\ntwo\n");

        Assert.Equal("2 f.txt\n", harness.Run("wc -l f.txt").Stdout);
    }

    [Fact]
    public void WcCountsEachOperandAndTotalsThem()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "one\ntwo\n");
        harness.Write("b.txt", "three\n");

        Assert.Equal("2 a.txt\n1 b.txt\n3 total\n", harness.Run("wc -l a.txt b.txt").Stdout);
    }

    [Fact]
    public void WcTotalsEveryColumnItWasAskedFor()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "one two\n");
        harness.Write("b.txt", "three\n");

        Assert.Equal("1 2 8 a.txt\n1 1 6 b.txt\n2 3 14 total\n", harness.Run("wc a.txt b.txt").Stdout);
    }

    [Fact]
    public void WcReportsAMissingOperandAndKeepsCountingTheRest()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "one\n");

        ShellResult result = harness.Run("wc -l a.txt missing.txt");

        Assert.Equal("1 a.txt\n1 total\n", result.Stdout);
        Assert.Equal("wc: missing.txt: No such file or directory\n", result.Stderr);
        Assert.Equal(1, result.ExitCode);
    }
}
