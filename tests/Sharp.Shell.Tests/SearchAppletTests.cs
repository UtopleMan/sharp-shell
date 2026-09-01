using Sharp.Shell;
using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
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

    [Theory]
    [InlineData("printf 'abcabc\\n' | grep -o abc", "abc\nabc\n")]
    [InlineData("printf 'a\\nbab\\n' | grep -o -n a", "1:a\n2:a\n")]
    [InlineData("printf 'aAa\\n' | grep -o -i a", "a\nA\na\n")]
    [InlineData("printf 'cat cats\\n' | grep -o -w cat", "cat\n")]
    [InlineData("printf 'aa\\n' | grep -o -c a", "1\n")]
    public void GrepReportsOnlyTheMatchedSpan(string commandLine, string expected)
    {
        using ShellHarness harness = new();

        Assert.Equal(expected, harness.Run(commandLine).Stdout);
    }

    // An empty match still makes the line count, so the status is zero even though nothing prints.
    [Fact]
    public void GrepPrintsNothingForAnEmptyMatchButStillSucceeds()
    {
        using ShellHarness harness = new();
        ShellResult result = harness.Run("printf 'abc\\n' | grep -o 'x*'");

        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void GrepNamesTheFileWhenSeveralAreSearchedWithMatchesOnly()
    {
        using ShellHarness harness = new();
        harness.Write("f1.txt", "a\n");
        harness.Write("f2.txt", "a\n");

        Assert.Equal("f1.txt:a\nf2.txt:a\n", harness.Run("grep -o a f1.txt f2.txt").Stdout);
    }

    // -o makes the match extent the answer, so the alternation rule the translator relaxes for a
    // whole-line grep applies again.
    [Fact]
    public void GrepWithMatchesOnlyRefusesAnAmbiguousAlternation()
    {
        IApplet grep = AppletRegistry.CreateDefault().Applets.Single(applet => applet.Name == "grep");

        Assert.True(grep.CheckFlags(["-E", "foo|foobar"]).IsSupported);
        Assert.False(grep.CheckFlags(["-o", "-E", "foo|foobar"]).IsSupported);
    }

    [Fact]
    public void GrepRefusesMatchesOnlyTogetherWithInversion()
    {
        IApplet grep = AppletRegistry.CreateDefault().Applets.Single(applet => applet.Name == "grep");

        Assert.False(grep.CheckFlags(["-o", "-v", "a"]).IsSupported);
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
    public void FindFiltersByNameIgnoringCase()
    {
        using ShellHarness harness = new();
        harness.Write("Report.TXT", "");
        harness.Write("notes.md", "");

        Assert.Equal("./Report.TXT\n", Listed(harness, "find . -iname '*.txt'"));
    }

    [Fact]
    public void FindFiltersByPath()
    {
        using ShellHarness harness = new();
        harness.Write("sub/b.txt", "");
        harness.Write("top.txt", "");

        Assert.Equal("./sub/b.txt\n", Listed(harness, "find . -path '*/sub/*'"));
    }

    [Fact]
    public void FindFiltersByFileType()
    {
        using ShellHarness harness = new();
        harness.Write("sub/b.txt", "");

        string output = Listed(harness, "find . -type f");

        Assert.Equal("./sub/b.txt\n", output);
    }

    [Fact]
    public void FindHonoursMinDepth()
    {
        using ShellHarness harness = new();
        harness.Write("sub/deep/c.txt", "");

        string output = Listed(harness, "find . -mindepth 2");

        Assert.Contains("./sub/deep", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\n./sub\n", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("find . -not -name '*.txt'")]
    [InlineData("find . ! -name '*.txt'")]
    public void FindNegatesThePredicate(string commandLine)
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "");
        harness.Write("b.md", "");

        string output = Listed(harness, commandLine);

        Assert.Contains("./b.md", output, StringComparison.Ordinal);
        Assert.DoesNotContain("a.txt", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FindAcceptsAnExplicitPrint()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "");

        Assert.Equal(Listed(harness, "find . -name '*.txt'"), Listed(harness, "find . -name '*.txt' -print"));
    }

    [Fact]
    public void FindWalksEveryRootItIsGiven()
    {
        using ShellHarness harness = new();
        harness.Write("one/a.txt", "");
        harness.Write("two/b.txt", "");

        string output = Listed(harness, "find one two");

        Assert.Contains("one/a.txt", output, StringComparison.Ordinal);
        Assert.Contains("two/b.txt", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FindReportsAMissingRootAndKeepsGoing()
    {
        using ShellHarness harness = new();
        harness.Write("here/a.txt", "");

        ShellResult result = harness.Run("find absent here");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("find: absent: No such file or directory", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("here/a.txt", result.Stdout.Replace(Path.DirectorySeparatorChar, '/'), StringComparison.Ordinal);
    }

    [Fact]
    public void FindRefusesARootOutsideTheWorkspace()
    {
        using ShellHarness harness = new();
        ShellResult result = harness.Run("find ..");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("find: ..: outside the workspace", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Stdout);
    }

    [Fact]
    public void FindNamesASingleFileRootWithoutDescending()
    {
        using ShellHarness harness = new();
        harness.Write("a.txt", "");

        Assert.Equal("a.txt\n", Listed(harness, "find a.txt"));
    }

    [Fact]
    public void FindRejectsAnActionItDoesNotImplement()
    {
        IApplet find = AppletRegistry.CreateDefault().Applets.Single(applet => applet.Name == "find");

        Assert.False(find.CheckFlags([".", "-delete"]).IsSupported);
    }

    // A predicate we do not implement must send the whole line native rather than run a walk that
    // silently ignores it — a half-honoured `find` prints the wrong files.
    [Theory]
    [InlineData("find . -newer stamp.txt")]
    [InlineData("find . -size +1k")]
    [InlineData("find . -exec rm {} ;")]
    public void FindEscalatesAPredicateItDoesNotImplement(string commandLine)
    {
        using ShellHarness harness = new();
        harness.Write("stamp.txt", "");

        Assert.Equal(ExecutionTier.Native, harness.Classified(commandLine).Classification.Tier);
    }

    private static string Listed(ShellHarness harness, string commandLine) =>
        harness.Run(commandLine).Stdout.Replace(Path.DirectorySeparatorChar, '/');

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
