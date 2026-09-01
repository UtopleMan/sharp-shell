using System.Diagnostics;
using Sharp.Shell.Text;
using Xunit;

namespace Sharp.Shell.Tests;

// POSIX takes the longest match at the leftmost start; .NET takes the first alternative that
// matches there. sed refuses a pattern where the two can disagree; awk has to answer, so these
// tests pin the recovery layer that makes the answer POSIX's.
public class LongestMatchTests
{
    [Fact]
    public void AlternationWhoseBranchesDifferInLengthTakesTheLongest()
    {
        AwkRegex pattern = Compile("foo|foobar");

        Assert.Equal((1, 6), pattern.Find("xfoobar", 0));
    }

    [Fact]
    public void TheShorterBranchStillWinsWhenTheLongerCannotMatch()
    {
        AwkRegex pattern = Compile("foo|foobar");

        Assert.Equal((1, 3), pattern.Find("xfoobaz", 0));
    }

    [Fact]
    public void APatternWithNoAlternationTakesTheFastPath()
    {
        PosixRegexProgram program = PosixRegexTranslator.TranslateForExtent("a*b");

        Assert.True(program.IsTranslated);
        Assert.False(program.RequiresLongestScan);
    }

    [Fact]
    public void AFixedLengthAlternationTakesTheFastPath()
    {
        PosixRegexProgram program = PosixRegexTranslator.TranslateForExtent("cat|dog");

        Assert.True(program.IsTranslated);
        Assert.False(program.RequiresLongestScan);
    }

    [Fact]
    public void AVariableLengthAlternationAsksForTheScan()
    {
        PosixRegexProgram program = PosixRegexTranslator.TranslateForExtent("foo|foobar");

        Assert.True(program.IsTranslated);
        Assert.True(program.RequiresLongestScan);
    }

    // The gsub loop is where a short match compounds: each replacement restarts the search after the
    // previous extent, so one wrong length shifts everything that follows.
    [Fact]
    public void RepeatedFindsWalkTheWholeInputTheWayGsubDoes()
    {
        AwkRegex pattern = Compile("a|ab");

        Assert.Equal([(0, 2), (2, 2), (4, 1)], AllMatches(pattern, "ababa"));
    }

    [Fact]
    public void AlternationInsideARepeatTakesTheLongestOverall()
    {
        AwkRegex pattern = Compile("(a|ab)+");

        Assert.Equal((0, 5), pattern.Find("ababa", 0));
    }

    [Fact]
    public void AlternationNestedInAGroupIsRecovered()
    {
        AwkRegex pattern = Compile("x(ab|abcd)y");

        Assert.Equal((0, 6), pattern.Find("xabcdy", 0));
    }

    [Fact]
    public void TheLeftmostStartWinsEvenWhenALaterStartMatchesLonger()
    {
        AwkRegex pattern = Compile("b|bcde");

        Assert.Equal((1, 1), pattern.Find("abxbcde", 0));
    }

    [Fact]
    public void AnEmptyMatchIsReportedAtTheLeftmostPosition()
    {
        AwkRegex pattern = Compile("x*");

        Assert.Equal((0, 0), pattern.Find("abc", 0));
        Assert.Equal((1, 2), pattern.Find("axxb", 1));
    }

    [Fact]
    public void AStartAnchorOnlyHoldsAtTheStartOfTheInput()
    {
        AwkRegex pattern = Compile("^(a|ab)");

        Assert.Equal((0, 2), pattern.Find("abab", 0));
        Assert.Null(pattern.Find("abab", 1));
    }

    [Fact]
    public void AnEndAnchorOnlyHoldsAtTheEndOfTheInput()
    {
        AwkRegex pattern = Compile("(b|ab)$");

        Assert.Equal((2, 2), pattern.Find("abab", 0));
    }

    [Fact]
    public void AnAnchorInsideOneBranchDoesNotLeakIntoTheOther()
    {
        AwkRegex pattern = Compile("(^ab|b)");

        Assert.Equal((0, 2), pattern.Find("abab", 0));
        Assert.Equal((1, 1), pattern.Find("abab", 1));
    }

    [Fact]
    public void IsMatchAnswersWithoutScanningForTheExtent()
    {
        AwkRegex pattern = Compile("foo|foobar");

        Assert.True(pattern.IsMatch("xfoobar"));
        Assert.False(pattern.IsMatch("xbar"));
    }

    [Fact]
    public void AnUntranslatablePatternIsRefusedRatherThanApproximated()
    {
        Assert.False(AwkRegex.TryCreate("[[.a.]]", out _, out string? refusal));
        Assert.Contains("collating", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AGnuExtensionIsNotAwk()
    {
        Assert.False(AwkRegex.TryCreate(@"\w+", out _, out string? refusal));
        Assert.Contains("GNU extension", refusal, StringComparison.Ordinal);
    }

    // A bounded pattern probes at most its own maximum length, so the scan cost does not grow with
    // the input. Without that bound this case is quadratic and never finishes.
    [Fact]
    public void ALongInputStaysWithinTheTimeBudget()
    {
        AwkRegex pattern = Compile("foo|foobar");
        string haystack = new string('x', 100_000) + "foobar";

        Stopwatch clock = Stopwatch.StartNew();
        (int Start, int Length)? found = pattern.Find(haystack, 0);
        clock.Stop();

        Assert.Equal((100_000, 6), found);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void ALongInputUnderAnUnboundedPatternStaysWithinTheTimeBudget()
    {
        AwkRegex pattern = Compile("(a|ab)+");
        string haystack = new string('a', 100_000);

        Stopwatch clock = Stopwatch.StartNew();
        (int Start, int Length)? found = pattern.Find(haystack, 0);
        clock.Stop();

        Assert.Equal((0, 100_000), found);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.ElapsedMilliseconds} ms");
    }

    private static AwkRegex Compile(string pattern)
    {
        Assert.True(AwkRegex.TryCreate(pattern, out AwkRegex? compiled, out string? refusal), refusal);
        return compiled!;
    }

    private static List<(int Start, int Length)> AllMatches(AwkRegex pattern, string input)
    {
        List<(int Start, int Length)> found = [];
        int position = 0;

        while (position <= input.Length && pattern.Find(input, position) is (int start, int length) match)
        {
            found.Add(match);
            position = length == 0 ? start + 1 : start + length;
        }

        return found;
    }
}
