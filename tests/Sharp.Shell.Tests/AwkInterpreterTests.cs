using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// Driven as real command lines, because the record loop, the field rules and the exit code all
// travel through the pipeline that runs them.
public class AwkInterpreterTests
{
    [Theory]
    [InlineData("printf 'a b c\\n' | awk '{print $2}'", "b\n")]
    [InlineData("printf 'a b c\\n' | awk '{print $0}'", "a b c\n")]
    [InlineData("printf 'a b c\\n' | awk '{print}'", "a b c\n")]
    [InlineData("printf 'a b c\\n' | awk '{print NF}'", "3\n")]
    [InlineData("printf 'a b c\\n' | awk '{print $NF}'", "c\n")]
    [InlineData("printf '  a   b  \\n' | awk '{print NF, $1, $2}'", "2 a b\n")]
    [InlineData("printf 'a b\\n' | awk '{print $5 \"|\"}'", "|\n")]
    [InlineData("printf 'a\\nb\\nc\\n' | awk 'END {print NR}'", "3\n")]
    [InlineData("printf 'a\\nb\\n' | awk '{print NR, $0}'", "1 a\n2 b\n")]
    [InlineData("printf 'a\\nb\\nc\\n' | awk 'NR == 2'", "b\n")]
    [InlineData("printf 'a\\nb\\nc\\n' | awk 'NR > 1 && NR <= 2'", "b\n")]
    [InlineData("printf 'one\\ntwo\\n' | awk '/two/'", "two\n")]
    [InlineData("printf 'one\\ntwo\\n' | awk '!/two/'", "one\n")]
    [InlineData("printf 'a\\nb\\nc\\nd\\n' | awk '/b/,/c/'", "b\nc\n")]
    [InlineData("printf 'a\\nb\\nc\\n' | awk '/b/,/b/'", "b\n")]
    public void RecordsFieldsAndPatterns(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    [Theory]
    [InlineData("printf 'a b c\\n' | awk '{$2 = \"X\"; print}'", "a X c\n")]
    [InlineData("printf 'a b\\n' | awk '{$4 = \"d\"; print; print NF}'", "a b  d\n4\n")]
    [InlineData("printf 'a b c\\n' | awk '{NF = 2; print}'", "a b\n")]
    [InlineData("printf 'a b\\n' | awk '{NF = 4; print NF; print $0 \"|\"}'", "4\na b  |\n")]
    [InlineData("printf 'a b c\\n' | awk 'BEGIN {OFS = \"-\"} {$1 = $1; print}'", "a-b-c\n")]
    [InlineData("printf 'a b c\\n' | awk 'BEGIN {OFS = \"-\"} {print $1, $2}'", "a-b\n")]
    [InlineData("printf 'a b c\\n' | awk '{$0 = \"x y\"; print NF, $2}'", "2 y\n")]
    [InlineData("printf 'a:b\\n' | awk -F: '{print $2}'", "b\n")]
    [InlineData("printf 'a.b\\n' | awk -F. '{print $1}'", "a\n")]
    [InlineData("printf 'a1b22c\\n' | awk -F'[0-9]+' '{print NF, $3}'", "3 c\n")]
    [InlineData("printf 'a\\tb\\n' | awk -F'\\t' '{print $2}'", "b\n")]
    [InlineData("printf 'a\\tb\\n' | awk -F t '{print NF}'", "2\n")]
    public void FieldAssignmentRebuildsTheRecord(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    // POSIX says a change to FS takes effect for the next record, so the first line is still split on
    // blanks — `$1` is the whole of it — and only the second splits on the colon.
    [Fact]
    public void ChangingTheSeparatorTakesEffectOnTheNextRecord()
    {
        Assert.Equal("a:b\nc\n", Out("printf 'a:b\\nc:b\\n' | awk '{FS = \":\"} {print $1}'"));
    }

    [Theory]
    [InlineData("printf 'a\\n\\nb\\nc\\n\\n\\nd\\n' | awk 'BEGIN {RS = \"\"} {print NR \":\" $0}'", "1:a\n2:b\nc\n3:d\n")]
    [InlineData("printf '\\n\\na\\nb\\n' | awk 'BEGIN {RS = \"\"} {print NF}'", "2\n")]
    [InlineData("printf 'a;b;c' | awk 'BEGIN {RS = \";\"} {print NR, $0}'", "1 a\n2 b\n3 c\n")]
    [InlineData("printf 'a;b' | awk 'BEGIN {RS = \";x\"} END {print NR}'", "2\n")]
    public void RecordSeparators(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    [Theory]
    [InlineData("awk 'BEGIN {for (i = 1; i <= 3; i++) print i}'", "1\n2\n3\n")]
    [InlineData("awk 'BEGIN {i = 0; while (i < 3) {i++}; print i}'", "3\n")]
    [InlineData("awk 'BEGIN {i = 0; do i++; while (i < 3); print i}'", "3\n")]
    [InlineData("awk 'BEGIN {for (i = 1; i <= 5; i++) {if (i == 3) continue; if (i == 5) break; print i}}'", "1\n2\n4\n")]
    [InlineData("awk 'BEGIN {a[1] = \"x\"; a[2] = \"y\"; n = 0; for (k in a) n++; print n}'", "2\n")]
    [InlineData("awk 'BEGIN {a[1, 2] = 3; if ((1, 2) in a) print \"yes\"}'", "yes\n")]
    [InlineData("awk 'BEGIN {a[1] = 1; delete a[1]; print length(a)}'", "0\n")]
    [InlineData("awk 'BEGIN {a[1] = 1; a[2] = 2; delete a; print length(a)}'", "0\n")]
    [InlineData("awk 'BEGIN {SUBSEP = \":\"; a[1, 2] = 3; for (k in a) print k}'", "1:2\n")]
    [InlineData("printf 'a\\nb\\n' | awk '{next; print \"never\"} END {print NR}'", "2\n")]
    public void ControlFlowAndArrays(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    [Theory]
    [InlineData("awk 'function f(n) {return n < 2 ? 1 : n * f(n - 1)} BEGIN {print f(5)}'", "120\n")]
    [InlineData("awk 'function fill(a) {a[\"k\"] = 7} BEGIN {fill(x); print x[\"k\"]}'", "7\n")]
    [InlineData("awk 'function bump(n) {n = 99} BEGIN {v = 1; bump(v); print v}'", "1\n")]
    [InlineData("awk 'function count(s,   parts, n) {n = split(s, parts, \":\"); return n} BEGIN {print count(\"a:b:c\")}'", "3\n")]
    [InlineData("awk 'function nothing() {} BEGIN {print nothing() \"|\"}'", "|\n")]
    public void UserFunctions(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    [Fact]
    public void RunawayRecursionIsAnErrorRatherThanACrash()
    {
        ShellResult result = Run("awk 'function f() {return f()} BEGIN {f()}'");

        Assert.Contains("nested too deep", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public void ExitStillRunsEndAndKeepsItsStatus()
    {
        ShellResult result = Run("printf 'a\\nb\\n' | awk 'NR == 1 {exit 3} END {print \"end\", NR}'");

        Assert.Equal("end 1\n", result.Stdout);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void ExitInsideEndStopsImmediately()
    {
        Assert.Equal(
            "first\nsecond\n",
            Out("awk 'BEGIN {print \"first\"; exit} END {print \"second\"; exit; print \"third\"}'"));
    }

    [Fact]
    public void ExitInBeginSkipsTheInputButNotEnd()
    {
        Assert.Equal("end 0\n", Out("printf 'a\\nb\\n' | awk 'BEGIN {exit} {print \"read\"} END {print \"end\", NR}'"));
    }

    [Fact]
    public void DivisionByZeroStops()
    {
        ShellResult result = Run("awk 'BEGIN {print 1 / 0}'");

        Assert.Contains("division by zero", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public void AProgramOfOnlyBeginRulesNeverReadsItsInput()
    {
        using ShellHarness harness = new();
        harness.Write("data.txt", "a\nb\n");

        Assert.Equal("1\n", harness.Run("awk 'BEGIN {print 1}' data.txt").Stdout);
    }

    [Fact]
    public void FilenameAndFileRecordNumberFollowTheFiles()
    {
        using ShellHarness harness = new();
        harness.Write("one.txt", "a\nb\n");
        harness.Write("two.txt", "c\n");

        Assert.Equal(
            "one.txt 1 1\none.txt 2 2\ntwo.txt 1 3\n",
            harness.Run("awk '{print FILENAME, FNR, NR}' one.txt two.txt").Stdout);
    }

    [Fact]
    public void NextfileMovesToTheFollowingFile()
    {
        using ShellHarness harness = new();
        harness.Write("one.txt", "a\nb\n");
        harness.Write("two.txt", "c\nd\n");

        Assert.Equal("a\nc\n", harness.Run("awk 'FNR == 1 {print; nextfile} {print \"extra\"}' one.txt two.txt").Stdout);
    }

    // An operand shaped like `name=value` is applied where it stands, so the same program sees a
    // different value over each file.
    [Fact]
    public void AssignmentOperandsTakeEffectBetweenFiles()
    {
        using ShellHarness harness = new();
        harness.Write("one.txt", "a\n");
        harness.Write("two.txt", "b\n");

        Assert.Equal("first a\nsecond b\n", harness.Run("awk '{print tag, $0}' tag=first one.txt tag=second two.txt").Stdout);
    }

    [Fact]
    public void RewritingArgvInBeginChangesWhichFilesAreRead()
    {
        using ShellHarness harness = new();
        harness.Write("one.txt", "a\n");
        harness.Write("two.txt", "b\n");

        Assert.Equal("b\n", harness.Run("awk 'BEGIN {ARGV[1] = \"two.txt\"; ARGC = 2} {print}' one.txt two.txt").Stdout);
    }

    [Fact]
    public void ADashOperandMeansStandardInput()
    {
        Assert.Equal("x\n", Out("printf 'x\\n' | awk '{print}' -"));
    }

    [Fact]
    public void AMissingFileIsReportedAndTheExitCodeSaysSo()
    {
        ShellResult result = Run("awk '{print}' nowhere.txt");

        Assert.Contains("can't open file nowhere.txt", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public void EnvironIsTheShellsExportedVariables()
    {
        Assert.Equal("hello\n", Out("v=hello; export v; awk 'BEGIN {print ENVIRON[\"v\"]}'"));
    }

    // ENVIRON is the environment, not the variable table: awk is a child process everywhere else,
    // and a variable the shell never exported is not one a child can see.
    [Fact]
    public void EnvironDoesNotShowAnUnexportedVariable()
    {
        Assert.Equal("[]\n", Out("v=hello; awk 'BEGIN {print \"[\" ENVIRON[\"v\"] \"]\"}'"));
    }

    // Laziness is per record: a consumer that stops reading stops the producer feeding awk. A BEGIN
    // rule that prints without end is bounded by cancellation instead, because there is no record
    // boundary inside it at which to hand output on.
    [Fact]
    public void AFinishedConsumerStopsTheProducerFeedingAwk()
    {
        using ShellHarness harness = new();
        CountingApplet counting = new();
        harness.Add(counting);

        ShellResult result = harness.Run("counting | awk '{print $2}' | head -3");

        Assert.Equal("1\n2\n3\n", result.Stdout);
        Assert.True(counting.Produced <= 8, $"the producer yielded {counting.Produced} lines for a head -3");
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
