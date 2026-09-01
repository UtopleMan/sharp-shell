using Sharp.Shell.Commands;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

public class SearchAppletTests
{
    [Fact]
    public void GrepFiltersItsInput()
    {
        using ShellHarness harness = new();

        Assert.Equal("bad\n", harness.Run("printf 'good\\nbad\\n' | grep bad").Stdout);
    }

    [Fact]
    public void GrepExitsOneWhenNothingMatches()
    {
        using ShellHarness harness = new();

        Assert.Equal(1, harness.Run("printf 'good\\n' | grep nothing").ExitCode);
    }

    [Fact]
    public void GrepPrefixesTheFileNameForSeveralFiles()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "hit\n");
        harness.Write("b.txt", "miss\n");

        Assert.Equal("a.txt:hit\n", harness.Run("grep hit a.txt b.txt").Stdout);
    }

    [Fact]
    public void GrepNumbersLinesWithDashN()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "one\ntwo\n");

        Assert.Equal("2:two\n", harness.Run("grep -n two a.txt").Stdout);
    }

    [Fact]
    public void GrepInvertsWithDashV()
    {
        using ShellHarness harness = new();

        Assert.Equal("good\n", harness.Run("printf 'good\\nbad\\n' | grep -v bad").Stdout);
    }

    [Fact]
    public void GrepIgnoresCaseWithDashI()
    {
        using ShellHarness harness = new();

        Assert.Equal("BAD\n", harness.Run("printf 'BAD\\n' | grep -i bad").Stdout);
    }

    [Fact]
    public void GrepCountsWithDashC()
    {
        using ShellHarness harness = new();

        Assert.Equal("2\n", harness.Run("printf 'a\\na\\nb\\n' | grep -c a").Stdout);
    }

    [Fact]
    public void GrepSeesFilesGitWouldIgnore()
    {
        using ShellHarness harness = new();
        harness.Write(".gitignore", "secret.txt\n");
        harness.Write("secret.txt", "needle\n");

        Assert.Equal("secret.txt:needle\n", harness.Run("grep -r needle .").Stdout.Replace("./", string.Empty));
    }

    [Fact]
    public void GrepAcceptsBundledShortFlags()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "one\nneedle\n");

        Assert.Equal("a.txt:2:needle\n", harness.Run("grep -rn needle .").Stdout.Replace("./", string.Empty));
    }

    [Theory]
    [InlineData("grep -r --include=*.cs needle .")]
    [InlineData("grep -r --include *.cs needle .")]
    public void GrepFiltersRecursiveSearchByFileGlob(string commandLine)
    {
        using ShellHarness harness = new();
        harness.Write("a.cs", "needle\n");
        harness.Write("b.txt", "needle\n");

        string output = harness.Run(commandLine).Stdout.Replace("./", string.Empty);

        Assert.Contains("a.cs", output, StringComparison.Ordinal);
        Assert.DoesNotContain("b.txt", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GrepRejectsAFlagItDoesNotImplement()
    {
        IApplet grep = AppletRegistry.CreateDefault().Applets.Single(applet => applet.Name == "grep");

        Assert.False(grep.CheckFlags(["-P", "x"]).IsSupported);
    }

    [Fact]
    public void FindListsEverythingBelowItsRoot()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "");
        harness.Write("sub/b.txt", "");

        string output = harness.Run("find .").Stdout.Replace(Path.DirectorySeparatorChar, '/');

        Assert.Contains("./a.txt", output, StringComparison.Ordinal);
        Assert.Contains("./sub/b.txt", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FindFiltersByName()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "");
        harness.Write("b.md", "");

        Assert.Equal("./a.txt\n", harness.Run("find . -name '*.txt'").Stdout.Replace(Path.DirectorySeparatorChar, '/'));
    }

    [Fact]
    public void FindFiltersByType()
    {
        using ShellHarness harness = new();
        harness.Write("sub/b.txt", "");

        string output = harness.Run("find . -type d").Stdout.Replace(Path.DirectorySeparatorChar, '/');

        Assert.Contains("./sub", output, StringComparison.Ordinal);
        Assert.DoesNotContain("b.txt", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FindHonoursMaxDepth()
    {
        using ShellHarness harness = new();
        harness.Write("sub/deep/c.txt", "");

        string output = harness.Run("find . -maxdepth 1").Stdout.Replace(Path.DirectorySeparatorChar, '/');

        Assert.Contains("./sub", output, StringComparison.Ordinal);
        Assert.DoesNotContain("deep", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FindStopsEarlyWhenItsConsumerDoes()
    {
        using ShellHarness harness = new();
        for (int index = 0; index < 200; index++)
        {
            harness.Write($"f{index}.txt", "");
        }

        Assert.Equal(2, harness.Run("find . | head -2").Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void FindRejectsAnActionItDoesNotImplement()
    {
        IApplet find = AppletRegistry.CreateDefault().Applets.Single(applet => applet.Name == "find");

        Assert.False(find.CheckFlags([".", "-delete"]).IsSupported);
    }

    // grep speaks POSIX now (plans/sed-support.md), not .NET: parentheses and braces are literals
    // until -E, and the GNU escapes repeat.
    [Theory]
    [InlineData("printf 'foo(bar)\\n' | grep 'foo(bar)'", "foo(bar)\n")]
    [InlineData("printf 'foobar\\n' | grep -E 'foo(bar)'", "foobar\n")]
    [InlineData("printf 'aaa\\n' | grep 'a\\+'", "aaa\n")]
    [InlineData("printf 'a+b\\n' | grep 'a+b'", "a+b\n")]
    [InlineData("printf 'x12y\\n' | grep '[[:digit:]]'", "x12y\n")]
    [InlineData("printf 'ab\\n' | grep -E 'a{1,2}b'", "ab\n")]
    [InlineData("printf 'a{1}\\n' | grep 'a{1}'", "a{1}\n")]
    public void GrepReadsPosixRegularExpressions(string commandLine, string expected)
    {
        using ShellHarness harness = new();

        Assert.Equal(expected, harness.Run(commandLine).Stdout);
    }

    [Fact]
    public void GrepMatchesWholeWordsWithDashW()
    {
        using ShellHarness harness = new();

        Assert.Equal("a foo b\n", harness.Run("printf 'a foo b\\nfoobar\\n' | grep -w foo").Stdout);
    }

    [Fact]
    public void GrepHidesFileNamesWithDashH()
    {
        using ShellHarness harness = new();
        harness.Write("one.txt", "match\n");
        harness.Write("two.txt", "match\n");

        Assert.Equal("one.txt:match\ntwo.txt:match\n", harness.Run("grep match one.txt two.txt").Stdout);
        Assert.Equal("match\nmatch\n", harness.Run("grep -h match one.txt two.txt").Stdout);
    }

    [Fact]
    public void GrepRefusesAPatternTheTranslatorWillNotVouchFor()
    {
        Assert.False(new GrepApplet().CheckFlags(["[[.a.]]"]).IsSupported);
        Assert.True(new GrepApplet().CheckFlags(["-F", "[[.a.]]"]).IsSupported);
    }
}
