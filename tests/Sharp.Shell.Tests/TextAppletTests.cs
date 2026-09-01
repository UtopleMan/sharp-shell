using Sharp.Shell.Commands;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

public class TextAppletTests
{
    private static string Out(string commandLine)
    {
        using ShellHarness harness = new();
        return harness.Run(commandLine).Stdout;
    }

    [Fact]
    public void SortOrdersLinesOrdinally()
    {
        Assert.Equal("a\nb\nc\n", Out("printf 'c\\na\\nb\\n' | sort"));
    }

    [Fact]
    public void SortReversesWithDashR()
    {
        Assert.Equal("c\nb\na\n", Out("printf 'c\\na\\nb\\n' | sort -r"));
    }

    [Fact]
    public void SortComparesNumericallyWithDashN()
    {
        Assert.Equal("2\n10\n", Out("printf '10\\n2\\n' | sort -n"));
    }

    [Fact]
    public void SortDedupesWithDashU()
    {
        Assert.Equal("a\nb\n", Out("printf 'b\\na\\nb\\n' | sort -u"));
    }

    [Fact]
    public void SortNumericallyReadsTheLeadingNumberOfALine()
    {
        Assert.Equal(
            "960 b\n99 a\n97 c\n5 d\n",
            Out("printf '99 a\\n960 b\\n97 c\\n5 d\\n' | sort -rn"));
    }

    [Fact]
    public void SortNumericallyTreatsALineWithNoLeadingNumberAsZero()
    {
        Assert.Equal(
            "-2\nabc\nx9\n3.5\n5\n",
            Out("printf 'abc\\n5\\n-2\\n3.5\\nx9\\n' | sort -n"));
    }

    [Fact]
    public void SortBreaksNumericTiesByComparingWholeLines()
    {
        Assert.Equal("5 z\n99 a\n99 b\n", Out("printf '99 b\\n99 a\\n5 z\\n' | sort -n"));
    }

    [Fact]
    public void SortDedupesOnTheKeyAndNotTheWholeLine()
    {
        Assert.Equal("99 b\n", Out("printf '99 b\\n99 a\\n' | sort -nu"));
    }

    [Fact]
    public void UniqCollapsesAdjacentDuplicates()
    {
        Assert.Equal("a\nb\n", Out("printf 'a\\na\\nb\\n' | uniq"));
    }

    [Fact]
    public void UniqCountsWithDashC()
    {
        Assert.Equal("2 a\n1 b\n", Out("printf 'a\\na\\nb\\n' | uniq -c"));
    }

    [Fact]
    public void UniqShowsOnlyDuplicatesWithDashD()
    {
        Assert.Equal("a\n", Out("printf 'a\\na\\nb\\n' | uniq -d"));
    }

    [Fact]
    public void CutTakesFieldsByDelimiter()
    {
        Assert.Equal("b\n", Out("printf 'a:b:c\\n' | cut -d : -f 2"));
    }

    [Fact]
    public void CutTakesAFieldRange()
    {
        Assert.Equal("b:c\n", Out("printf 'a:b:c\\n' | cut -d : -f 2-3"));
    }

    [Fact]
    public void CutTakesCharacters()
    {
        Assert.Equal("ab\n", Out("printf 'abcdef\\n' | cut -c 1-2"));
    }

    [Fact]
    public void TrTranslatesCharacters()
    {
        Assert.Equal("XbcX\n", Out("printf 'abca\\n' | tr a X"));
    }

    [Fact]
    public void TrTranslatesRanges()
    {
        Assert.Equal("ABC\n", Out("printf 'abc\\n' | tr 'a-z' 'A-Z'"));
    }

    [Fact]
    public void TrDeletesWithDashD()
    {
        Assert.Equal("bc\n", Out("printf 'abca\\n' | tr -d a"));
    }

    [Fact]
    public void TrSqueezesWithDashS()
    {
        Assert.Equal("aba\n", Out("printf 'aabbba\\n' | tr -s ab"));
    }

    [Theory]
    [InlineData("sort", "-R")]
    [InlineData("uniq", "-i")]
    [InlineData("cut", "-b")]
    [InlineData("tr", "-c")]
    public void EachTextAppletRefusesAFlagItDoesNotImplement(string name, string flag)
    {
        IApplet applet = AppletRegistry.CreateDefault().Applets.Single(candidate => candidate.Name == name);

        Assert.False(applet.CheckFlags([flag, "x"]).IsSupported);
    }
}
