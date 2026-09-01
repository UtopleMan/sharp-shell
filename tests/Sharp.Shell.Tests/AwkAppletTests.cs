using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// The command line rather than the language: which words are options, which are the program, and
// which are input.
public class AwkAppletTests
{
    [Theory]
    [InlineData("printf 'a:b\\n' | awk -F: '{print $2}'", "b\n")]
    [InlineData("printf 'a:b\\n' | awk -F : '{print $2}'", "b\n")]
    [InlineData("printf 'a:b\\n' | awk -- '{print $0}'", "a:b\n")]
    [InlineData("printf 'a\\tb\\n' | awk -F'\\t' '{print $2}'", "b\n")]
    [InlineData("printf 'a,b\\n' | awk -v FS=, '{print $2}'", "b\n")]
    [InlineData("awk -v x=1 -v y=2 'BEGIN {print x + y}'", "3\n")]
    [InlineData("awk -v 'x=a\\tb' 'BEGIN {print length(x)}'", "3\n")]
    [InlineData("awk -vx=5 'BEGIN {print x}'", "5\n")]
    [InlineData("printf 'x\\n' | awk '{print}' -", "x\n")]
    [InlineData("printf 'x\\n' | nawk '{print}'", "x\n")]
    public void OptionsAreReadTheWayAwkReadsThem(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    // awk's escape table, which three callers share: string literals, regex literals, and -v.
    // An escape awk does not define loses its backslash, so `\q` is a `q`.
    [Theory]
    [InlineData(@"awk 'BEGIN {printf ""a\tb""}'", "a\tb")]
    [InlineData(@"awk 'BEGIN {printf ""a\nb""}'", "a\nb")]
    [InlineData(@"awk 'BEGIN {printf ""a\rb""}'", "a\rb")]
    [InlineData(@"awk 'BEGIN {printf ""a\bb""}'", "a\bb")]
    [InlineData(@"awk 'BEGIN {printf ""a\fb""}'", "a\fb")]
    [InlineData(@"awk 'BEGIN {printf ""a\vb""}'", "a\vb")]
    [InlineData(@"awk 'BEGIN {printf ""a\ab""}'", "a\ab")]
    [InlineData(@"awk 'BEGIN {printf ""a\\b""}'", @"a\b")]
    [InlineData(@"awk 'BEGIN {printf ""a\""b""}'", "a\"b")]
    [InlineData(@"awk 'BEGIN {printf ""a\/b""}'", "a/b")]
    [InlineData(@"awk 'BEGIN {printf ""a\qb""}'", "aqb")]
    [InlineData(@"awk 'BEGIN {printf ""a\101b""}'", "aAb")]
    [InlineData(@"awk 'BEGIN {printf ""a\0b""}'", "a\0b")]
    public void EscapesDecodeTheWayAwkDecodesThem(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    // A trailing `e` is only an exponent when digits follow it; otherwise the number ends and the
    // `e` starts a name.
    [Theory]
    [InlineData("awk 'BEGIN {print 1e3}'", "1000\n")]
    [InlineData("awk 'BEGIN {print 1E3}'", "1000\n")]
    [InlineData("awk 'BEGIN {print 1e+3}'", "1000\n")]
    [InlineData("awk 'BEGIN {print 1e-3}'", "0.001\n")]
    [InlineData("awk 'BEGIN {e = 5; print 1 e}'", "15\n")]
    [InlineData("awk 'BEGIN {ex = 5; print 1ex}'", "15\n")]
    public void ExponentsAreReadOnlyWhenDigitsFollow(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    // `--` reaches the program as an operand only when it comes before it; after the program every
    // word is input, which is what stops `awk '{print}' -F` from losing its file.
    [Fact]
    public void WordsAfterTheProgramAreNeverOptions()
    {
        using ShellHarness harness = new();
        harness.Write("-x.txt", "kept\n");

        Assert.Equal("kept\n", harness.Run("awk '{print}' ./-x.txt").Stdout);
    }

    [Fact]
    public void ProgramFilesAreReadInCommandLineOrder()
    {
        using ShellHarness harness = new();
        harness.Write("first.awk", "BEGIN { print \"one\" }\n");
        harness.Write("second.awk", "BEGIN { print \"two\" }\n");

        Assert.Equal("one\ntwo\n", harness.Run("awk -f first.awk -f second.awk").Stdout);
        Assert.Equal("two\none\n", harness.Run("awk -f second.awk -f first.awk").Stdout);
    }

    [Fact]
    public void WithAProgramFileEveryOperandIsInput()
    {
        using ShellHarness harness = new();
        harness.Write("show.awk", "{ print FILENAME, $0 }\n");
        harness.Write("data.txt", "a\n");

        Assert.Equal("data.txt a\n", harness.Run("awk -f show.awk data.txt").Stdout);
    }

    [Fact]
    public void AMissingProgramFileIsReported()
    {
        using ShellHarness harness = new();
        ShellResult result = harness.Run("awk -f nowhere.awk");

        Assert.Contains("can't open file nowhere.awk", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public void NoProgramAtAllIsAUsageError()
    {
        using ShellHarness harness = new();
        ShellResult result = harness.Run("awk");

        Assert.Contains("usage: awk", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public void AnOptionWithoutItsValueIsAUsageError()
    {
        using ShellHarness harness = new();
        ShellResult result = harness.Run("awk -F");

        Assert.Contains("requires an argument", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(2, result.ExitCode);
    }

    [Theory]
    [InlineData("--posix")]
    [InlineData("--traditional")]
    [InlineData("--re-interval")]
    [InlineData("--csv")]
    [InlineData("--version")]
    [InlineData("--help")]
    [InlineData("-W")]
    [InlineData("-d")]
    public void AFlagThisDialectDoesNotImplementIsRefusedRatherThanIgnored(string flag)
    {
        FlagSupport support = new AwkApplet("awk").CheckFlags([flag, "{print}"]);

        Assert.False(support.IsSupported);
        Assert.NotNull(support.UnsupportedFlag);
    }

    [Fact]
    public void BothNamesAreOneImplementation()
    {
        Assert.Equal(Out("printf 'a b\\n' | awk '{print $2}'"), Out("printf 'a b\\n' | nawk '{print $2}'"));
    }

    [Fact]
    public void TheProgramIsTheOnlyArgumentThatHasToBeLiteral()
    {
        Assert.Equal([0], new AwkApplet("awk").ProgramTextArguments(["{print}", "file.txt"]));
        Assert.Equal([1], new AwkApplet("awk").ProgramTextArguments(["-F:", "{print}", "file.txt"]));
        Assert.Equal([2], new AwkApplet("awk").ProgramTextArguments(["-v", "x=1", "{print}"]));
        Assert.Empty(new AwkApplet("awk").ProgramTextArguments(["-f", "p.awk", "file.txt"]));
    }

    [Theory]
    [InlineData("{print}", false)]
    [InlineData("{print > \"/dev/stdout\"}", false)]
    [InlineData("{print > \"/dev/stderr\"}", false)]
    [InlineData("{print > \"out.txt\"}", true)]
    [InlineData("{print >> \"log.txt\"}", true)]
    [InlineData("{printf \"%s\", $0 > \"out.txt\"}", true)]
    [InlineData("{print > (\"f\" NR)}", true)]
    public void MutationIsAnsweredForTheInvocationBeingRun(string program, bool expected)
    {
        Assert.Equal(expected, new AwkApplet("awk").MutatesWith([program]));
    }

    // A program that cannot be read at classification time has to be assumed to mutate.
    [Fact]
    public void AProgramFileIsAssumedToMutate()
    {
        Assert.True(new AwkApplet("awk").MutatesWith(["-f", "p.awk"]));
    }

    private static string Out(string commandLine)
    {
        using ShellHarness harness = new();
        return harness.Run(commandLine).Stdout;
    }
}
