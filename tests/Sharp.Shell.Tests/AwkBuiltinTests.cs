using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// Every expectation here was taken from `/usr/bin/awk` and `gawk --posix` running the same program;
// the handful of cases where those two disagree are named in their own tests.
public class AwkBuiltinTests
{
    [Theory]
    [InlineData("%d", "42", "42")]
    [InlineData("%d", "-42", "-42")]
    [InlineData("%d", "3.9", "3")]
    [InlineData("%d", "-3.9", "-3")]
    [InlineData("%5d", "42", "   42")]
    [InlineData("%-5d", "42", "42   ")]
    [InlineData("%05d", "42", "00042")]
    [InlineData("%+d", "42", "+42")]
    [InlineData("% d", "42", " 42")]
    [InlineData("%.3d", "42", "042")]
    [InlineData("%5.3d", "42", "  042")]
    [InlineData("%i", "42", "42")]
    [InlineData("%o", "8", "10")]
    [InlineData("%#o", "8", "010")]
    [InlineData("%x", "255", "ff")]
    [InlineData("%X", "255", "FF")]
    [InlineData("%#x", "255", "0xff")]
    [InlineData("%u", "42", "42")]
    [InlineData("%c", "65", "A")]
    [InlineData("%c", "\"BC\"", "B")]
    [InlineData("%5c", "65", "    A")]
    [InlineData("%s", "\"hello\"", "hello")]
    [InlineData("%10s", "\"hello\"", "     hello")]
    [InlineData("%-10s", "\"hello\"", "hello     ")]
    [InlineData("%.3s", "\"hello\"", "hel")]
    [InlineData("%e", "1234.5678", "1.234568e+03")]
    [InlineData("%E", "1234.5678", "1.234568E+03")]
    [InlineData("%.2e", "1234.5678", "1.23e+03")]
    [InlineData("%f", "1234.5678", "1234.567800")]
    [InlineData("%.2f", "1234.5678", "1234.57")]
    [InlineData("%10.2f", "1234.5678", "   1234.57")]
    [InlineData("%-10.2f", "1234.5678", "1234.57   ")]
    [InlineData("%010.2f", "1234.5678", "0001234.57")]
    [InlineData("%g", "1234.5678", "1234.57")]
    [InlineData("%g", "0.00001234", "1.234e-05")]
    [InlineData("%G", "0.00001234", "1.234E-05")]
    [InlineData("%.3g", "1234.5678", "1.23e+03")]
    [InlineData("%#g", "1.5", "1.50000")]
    [InlineData("%g", "100000", "100000")]
    [InlineData("%g", "1000000", "1e+06")]
    [InlineData("%d", "\"abc\"", "0")]
    [InlineData("%s", "1/3", "0.333333")]
    [InlineData("%d", "2147483648", "2147483648")]
    [InlineData("%s", "2^53", "9007199254740992")]
    [InlineData("%.0f", "2.5", "2")]
    [InlineData("%.0f", "3.5", "4")]
    public void PrintfFollowsTheConversionTable(string format, string argument, string expected)
    {
        Assert.Equal(expected, Out($"awk 'BEGIN {{printf \"{format}\", {argument}}}'"));
    }

    [Theory]
    [InlineData("awk 'BEGIN {printf \"%*d\", 5, 42}'", "   42")]
    [InlineData("awk 'BEGIN {printf \"%.*f\", 2, 3.14159}'", "3.14")]
    [InlineData("awk 'BEGIN {printf \"%-*d\", 5, 42}'", "42   ")]
    [InlineData("awk 'BEGIN {printf \"%%\"}'", "%")]
    [InlineData("awk 'BEGIN {printf \"%s|%s\", 1, 2, 3}'", "1|2")]
    [InlineData("awk 'BEGIN {printf \"plain\"}'", "plain")]
    public void PrintfHandlesStarsAndSpareArguments(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    // POSIX says a missing argument is an empty string or a zero. Both reference implementations stop
    // with an error instead, and a format that silently prints a blank where a value was meant is the
    // failure this tier exists to avoid — so the oracles win.
    [Fact]
    public void RunningOutOfPrintfArgumentsIsAnError()
    {
        ShellResult result = Run("awk 'BEGIN {printf \"%s %s\\n\", \"one\"}'");

        Assert.Contains("not enough arguments", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(2, result.ExitCode);
    }

    [Theory]
    [InlineData("substr(\"hello\", 2)", "ello")]
    [InlineData("substr(\"hello\", 2, 2)", "el")]
    [InlineData("substr(\"hello\", 0)", "hello")]
    [InlineData("substr(\"hello\", 0, 2)", "he")]
    [InlineData("substr(\"hello\", -1, 3)", "hel")]
    [InlineData("substr(\"hello\", 1.5, 2.5)", "he")]
    [InlineData("substr(\"hello\", 2.3, 2.3)", "el")]
    [InlineData("substr(\"hello\", 3.7, 1.2)", "l")]
    [InlineData("substr(\"hello\", 2, -1)", "")]
    [InlineData("substr(\"hello\", 10)", "")]
    [InlineData("substr(\"hello\", 4, 100)", "lo")]
    public void SubstringTruncatesItsArgumentsAndClampsItsRange(string call, string expected)
    {
        Assert.Equal(expected + "\n", Out($"awk 'BEGIN {{print {call}}}'"));
    }

    [Theory]
    [InlineData("awk 'BEGIN {s = \"ab\"; print gsub(/a*/, \"-\", s), s}'", "2 -b-\n")]
    [InlineData("awk 'BEGIN {s = \"aaa\"; print gsub(/a*/, \"X\", s), s}'", "1 X\n")]
    [InlineData("awk 'BEGIN {s = \"abc\"; print gsub(/x*/, \"-\", s), s}'", "4 -a-b-c-\n")]
    [InlineData("awk 'BEGIN {s = \"hello\"; print gsub(/l/, \"[&]\", s), s}'", "2 he[l][l]o\n")]
    [InlineData("awk 'BEGIN {s = \"hello\"; print gsub(/l/, \"[\\\\&]\", s), s}'", "2 he[&][&]o\n")]
    [InlineData("awk 'BEGIN {s = \"hello\"; print sub(/l/, \"L\", s), s}'", "1 heLlo\n")]
    [InlineData("printf 'aaa\\n' | awk '{gsub(/a/, \"b\"); print}'", "bbb\n")]
    [InlineData("printf 'a b\\n' | awk '{sub(/ /, \"-\"); print}'", "a-b\n")]
    public void SubstitutionCountsAndReplacementEscapes(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    [Theory]
    [InlineData("awk 'BEGIN {print split(\"a:b:c\", p, \":\"), p[1], p[3]}'", "3 a c\n")]
    [InlineData("awk 'BEGIN {print split(\"  a  b  \", p), p[1], p[2]}'", "2 a b\n")]
    [InlineData("awk 'BEGIN {print split(\"abc\", p, \"\"), p[1], p[3]}'", "3 a c\n")]
    [InlineData("awk 'BEGIN {print split(\"\", p, \":\")}'", "0\n")]
    [InlineData("awk 'BEGIN {print split(\"a1b22c\", p, /[0-9]+/), p[3]}'", "3 c\n")]
    [InlineData("awk 'BEGIN {print index(\"hello\", \"ll\"), index(\"hello\", \"z\")}'", "3 0\n")]
    [InlineData("awk 'BEGIN {print match(\"hello\", /l+/), RSTART, RLENGTH}'", "3 3 2\n")]
    [InlineData("awk 'BEGIN {print match(\"hello\", /z/), RSTART, RLENGTH}'", "0 0 -1\n")]
    [InlineData("awk 'BEGIN {print length(\"hello\"), length()}'", "5 0\n")]
    [InlineData("awk 'BEGIN {a[1] = 1; a[2] = 2; print length(a)}'", "2\n")]
    [InlineData("awk 'BEGIN {print toupper(\"aBc\"), tolower(\"aBc\")}'", "ABC abc\n")]
    [InlineData("awk 'BEGIN {print sprintf(\"%03d\", 7)}'", "007\n")]
    [InlineData("printf 'abc\\n' | awk '{print length}'", "3\n")]
    public void StringBuiltins(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    [Theory]
    [InlineData("awk 'BEGIN {print int(-3.9), int(3.9)}'", "-3 3\n")]
    [InlineData("awk 'BEGIN {print 7 % 3, -7 % 3}'", "1 -1\n")]
    [InlineData("awk 'BEGIN {print sqrt(9), exp(0), log(1)}'", "3 1 0\n")]
    [InlineData("awk 'BEGIN {printf \"%.4f %.4f\", sin(0), cos(0)}'", "0.0000 1.0000")]
    [InlineData("awk 'BEGIN {printf \"%.4f\", atan2(0, -1)}'", "3.1416")]
    public void MathBuiltins(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    // The generator is ours, so the sequence is not compared with any awk — only its reproducibility
    // and its range are, which is what a program can actually rely on.
    [Fact]
    public void TheSameSeedGivesTheSameSequence()
    {
        string first = Out("awk 'BEGIN {srand(7); for (i = 0; i < 3; i++) printf \"%.6f \", rand()}'");
        string again = Out("awk 'BEGIN {srand(7); for (i = 0; i < 3; i++) printf \"%.6f \", rand()}'");

        Assert.Equal(first, again);
        Assert.NotEqual(first, Out("awk 'BEGIN {srand(8); for (i = 0; i < 3; i++) printf \"%.6f \", rand()}'"));
    }

    [Fact]
    public void RandomValuesStayInsideTheUnitInterval()
    {
        Assert.Equal("ok\n", Out("awk 'BEGIN {srand(1); for (i = 0; i < 100; i++) {r = rand(); if (r < 0 || r >= 1) {print \"bad\"; exit}}; print \"ok\"}'"));
    }

    [Fact]
    public void SeedingReturnsThePreviousSeed()
    {
        Assert.Equal("5\n", Out("awk 'BEGIN {srand(5); print srand(9)}'"));
    }

    // Every refused builtin is stopped at parse time, so the interpreter never has to check again:
    // the flag check refuses the whole command line before it can run, and the applet refuses again
    // if something calls it directly.
    [Fact]
    public void ARefusedBuiltinNeverReachesTheInterpreter()
    {
        FlagSupport support = new AwkApplet("awk").CheckFlags(["BEGIN {system(\"ls\")}"]);

        Assert.False(support.IsSupported);
        Assert.Equal("system()", support.UnsupportedFlag);

        List<string> errors = [];
        AppletRun run = new AwkApplet("awk").Run(AppletContexts.For(
            ["BEGIN {system(\"ls\")}"],
            TextStream.Empty,
            new ShellState(Path.GetTempPath()),
            errors.Add));

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("unsupported construct: system()", string.Concat(errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertFormatChangesHowNumbersJoinStrings()
    {
        Assert.Equal("0.33\n", Out("awk 'BEGIN {CONVFMT = \"%.2g\"; print (1/3) \"\"}'"));
    }

    [Fact]
    public void OutputFormatChangesHowPrintShowsNumbers()
    {
        Assert.Equal("0.33\n", Out("awk 'BEGIN {OFMT = \"%.2g\"; print 1/3}'"));
    }

    private static string Out(string commandLine)
    {
        using ShellHarness harness = new();
        return harness.Run(commandLine).Stdout;
    }

    private static ShellResult Run(string commandLine)
    {
        using ShellHarness harness = new();
        return harness.Run(commandLine);
    }
}
