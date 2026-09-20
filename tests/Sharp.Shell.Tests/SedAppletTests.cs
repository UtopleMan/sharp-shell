using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// Driven as real command lines, because that is how sed is used and because the pipeline is part of
// what is being tested: laziness, the trailing newline, and the exit code all travel through it.
public class SedAppletTests
{
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

    [Theory]
    [InlineData("printf 'a\\nb\\nc\\n' | sed -n '2p'", "b\n")]
    [InlineData("printf 'a\\nb\\nc\\n' | sed -n '1,2p'", "a\nb\n")]
    [InlineData("printf 'a\\nb\\nc\\n' | sed -n '$p'", "c\n")]
    [InlineData("printf 'a\\nb\\nc\\n' | sed -n '2,$p'", "b\nc\n")]
    [InlineData("printf 'a\\nb\\nc\\nd\\n' | sed -n '1p;3p'", "a\nc\n")]
    [InlineData("printf 'a\\nb\\nc\\nd\\n' | sed -n '2,+1p'", "b\nc\n")]
    [InlineData("printf 'a\\nb\\nc\\nd\\n' | sed -n '1~2p'", "a\nc\n")]
    [InlineData("printf 'a\\nb\\nc\\nd\\n' | sed -n '/b/,/c/p'", "b\nc\n")]
    [InlineData("printf 'a\\nb\\nc\\n' | sed -n '2!p'", "a\nc\n")]
    [InlineData("printf 'a\\nb\\nc\\n' | sed '2d'", "a\nc\n")]
    [InlineData("printf 'a\\nb\\nc\\n' | sed -n '0,/b/p'", "a\nb\n")]
    public void AddressesSelectTheRightLines(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    [Theory]
    [InlineData("printf 'aaa\\n' | sed 's/a/b/'", "baa\n")]
    [InlineData("printf 'aaa\\n' | sed 's/a/b/g'", "bbb\n")]
    [InlineData("printf 'aaa\\n' | sed 's/a/b/2'", "aba\n")]
    [InlineData("printf 'aaaa\\n' | sed 's/a/b/2g'", "abbb\n")]
    [InlineData("printf 'ab\\n' | sed 's/a/[&]/'", "[a]b\n")]
    [InlineData("printf 'ab\\n' | sed 's/\\(a\\)b/\\1\\1/'", "aa\n")]
    [InlineData("printf 'ab\\n' | sed 's/.*/\\U&/'", "AB\n")]
    [InlineData("printf 'AB\\n' | sed 's/.*/\\l&/'", "aB\n")]
    [InlineData("printf 'a.b\\n' | sed 's/a\\.b/x/'", "x\n")]
    [InlineData("printf 'a/b\\n' | sed 's|/|-|'", "a-b\n")]
    [InlineData("printf 'foo\\n' | sed -n 's/o/0/gp'", "f00\n")]
    [InlineData("printf 'a b\\n' | sed 's/ \\+/_/'", "a_b\n")]
    [InlineData("printf 'abc\\n' | sed 'y/abc/xyz/'", "xyz\n")]
    public void SubstitutionAndTransliteration(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    [Theory]
    [InlineData("printf 'a\\nb\\nc\\n' | sed -n '1!G;h;$p'", "c\nb\na\n")]
    [InlineData("printf 'a\\nb\\nc\\n' | sed ':a;N;$!ba;s/\\n/,/g'", "a,b,c\n")]
    [InlineData("printf 'a\\nb\\n' | sed 'N;s/\\n/-/'", "a-b\n")]
    [InlineData("printf 'a\\nb\\nc\\n' | sed -n 'N;P;D'", "a\nb\n")]
    [InlineData("printf 'a\\nb\\n' | sed -n '$!{N};s/\\n/+/p'", "a+b\n")]
    [InlineData("printf 'aaa\\n' | sed ':a;s/a/b/;ta'", "bbb\n")]
    [InlineData("printf 'a\\nb\\n' | sed 'x;$!d;x;G'", "b\na\n")]
    public void HoldSpaceAndBranching(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    [Theory]
    [InlineData("printf 'a\\nb\\n' | sed '1a inserted'", "a\ninserted\nb\n")]
    [InlineData("printf 'a\\nb\\n' | sed '2i inserted'", "a\ninserted\nb\n")]
    [InlineData("printf 'a\\nb\\n' | sed '1c changed'", "changed\nb\n")]
    [InlineData("printf 'a\\nb\\nc\\n' | sed '1,2c changed'", "changed\nc\n")]
    [InlineData("printf 'a\\nb\\n' | sed -n '='", "1\n2\n")]
    [InlineData("printf 'a\\tb\\n' | sed -n 'l'", "a\\tb$\n")]
    [InlineData("printf 'a\\nb\\n' | sed -n '2{p;p}'", "b\nb\n")]
    [InlineData("printf 'a\\nb\\n' | sed 'z'", "\n\n")]
    public void OutputCommands(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    [Fact]
    public void AMissingTrailingNewlineSurvives()
    {
        Assert.Equal("a\nb", Out("printf 'a\\nb' | sed -n 'p'"));
        Assert.Equal("b", Out("printf 'a\\nb' | sed -n '$p'"));
        Assert.Equal("a\nb\n", Out("printf 'a\\nb\\n' | sed -n 'p'"));
    }

    [Fact]
    public void QuitCarriesItsExitCodeAndStopsReading()
    {
        ShellResult result = Run("printf 'a\\nb\\nc\\n' | sed '2q5'");

        Assert.Equal("a\nb\n", result.Stdout);
        Assert.Equal(5, result.ExitCode);
        Assert.Equal(string.Empty, Out("printf 'a\\nb\\n' | sed '1Q'"));
    }

    [Fact]
    public void NullSeparatedRecords()
    {
        Assert.Equal("x\0b\0", Out("printf 'a\\0b\\0' | sed -z 's/a/x/'"));
    }

    [Fact]
    public void ExtendedRegularExpressions()
    {
        Assert.Equal("x\n", Out("printf 'abab\\n' | sed -E 's/(ab)+/x/'"));
        Assert.Equal("x\n", Out("printf '1234\\n' | sed -r 's/[[:digit:]]{2,}/x/'"));
    }

    [Fact]
    public void ReadsFilesAsOneStreamUnlessSeparated()
    {
        using ShellHarness harness = new();
        harness.Write("one.txt", "a\nb\n");
        harness.Write("two.txt", "c\nd\n");

        Assert.Equal("d\n", harness.Run("sed -n '$p' one.txt two.txt").Stdout);
        Assert.Equal("b\nd\n", harness.Run("sed -n -s '$p' one.txt two.txt").Stdout);
        Assert.Equal("a\nc\n", harness.Run("sed -n -s '1p' one.txt two.txt").Stdout);
    }

    [Fact]
    public void MissingInputFileIsReportedAndSetsExitTwo()
    {
        using ShellHarness harness = new();
        ShellResult result = harness.Run("sed -n '1p' absent.txt");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("can't read absent.txt", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void EditsInPlaceInBothDialects()
    {
        using ShellHarness harness = new();
        harness.Write("gnu.txt", "a\n");
        harness.Write("bsd.txt", "a\n");

        harness.Run("sed -i 's/a/b/' gnu.txt");
        harness.Run("sed -i '' 's/a/b/' bsd.txt");

        Assert.Equal("b\n", File.ReadAllText(Path.Combine(harness.Root, "gnu.txt")));
        Assert.Equal("b\n", File.ReadAllText(Path.Combine(harness.Root, "bsd.txt")));
    }

    [Fact]
    public void InPlaceWithASuffixKeepsABackup()
    {
        using ShellHarness harness = new();
        harness.Write("kept.txt", "a\n");

        harness.Run("sed -i.bak 's/a/b/' kept.txt");

        Assert.Equal("b\n", File.ReadAllText(Path.Combine(harness.Root, "kept.txt")));
        Assert.Equal("a\n", File.ReadAllText(Path.Combine(harness.Root, "kept.txt.bak")));
    }

    [Fact]
    public void ScriptFileIsRead()
    {
        using ShellHarness harness = new();
        harness.Write("script.sed", "s/a/b/\n");
        harness.Write("input.txt", "a\n");

        Assert.Equal("b\n", harness.Run("sed -f script.sed input.txt").Stdout);
    }

    [Fact]
    public void WriteAndReadCommandsStayInsideTheWorkspace()
    {
        using ShellHarness harness = new();
        harness.Write("input.txt", "a\nb\n");
        harness.Write("extra.txt", "from-file\n");

        Assert.Equal("a\nfrom-file\nb\n", harness.Run("sed '1r extra.txt' input.txt").Stdout);

        harness.Run("sed -n '/a/w kept.txt' input.txt");
        Assert.Equal("a\n", File.ReadAllText(Path.Combine(harness.Root, "kept.txt")));

        ShellResult escape = harness.Run("sed -n '/a/w ../escaped.txt' input.txt");
        Assert.Equal(4, escape.ExitCode);
        Assert.False(File.Exists(Path.Combine(harness.Root, "..", "escaped.txt")));
    }

    [Fact]
    public void WritingToStandardOutputGoesToTheStream()
    {
        Assert.Equal("a\na\n", Out("printf 'a\\n' | sed 'w /dev/stdout'"));
    }

    // `R` takes one line per cycle and keeps its position between them, which is the whole
    // difference from `r` and the only thing that exercises the per-path line reader.
    [Fact]
    public void ReadLineInterleavesTheNamedFile()
    {
        using ShellHarness harness = new();
        harness.Write("lines.txt", "x\ny\n");

        Assert.Equal("a\nx\nb\ny\nc\n", harness.Run("printf 'a\\nb\\nc\\n' | sed 'R lines.txt'").Stdout);
    }

    [Fact]
    public void ReadLineStopsWhenTheNamedFileRunsOut()
    {
        using ShellHarness harness = new();
        harness.Write("lines.txt", "x\n");

        Assert.Equal("a\nx\nb\nc\n", harness.Run("printf 'a\\nb\\nc\\n' | sed 'R lines.txt'").Stdout);
    }

    [Fact]
    public void ReadLineFromAMissingFileAddsNothing()
    {
        using ShellHarness harness = new();

        Assert.Equal("a\n", harness.Run("printf 'a\\n' | sed 'R absent.txt'").Stdout);
    }

    [Fact]
    public void ReadLineOutsideTheWorkspaceIsRefused()
    {
        using ShellHarness harness = new();
        ShellResult result = harness.Run("printf 'a\\n' | sed 'R ../outside.txt'");

        Assert.Equal(4, result.ExitCode);
        Assert.Contains("outside the workspace", result.Stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("printf 'a\\tb\\n' | sed -n 'l'", "a\\tb$\n")]
    [InlineData("printf 'a\\\\b\\n' | sed -n 'l'", "a\\\\b$\n")]
    [InlineData("printf 'a\\rb\\n' | sed -n 'l'", "a\\rb$\n")]
    [InlineData("printf 'a\\ab\\n' | sed -n 'l'", "a\\ab$\n")]
    [InlineData("printf 'a\\bb\\n' | sed -n 'l'", "a\\bb$\n")]
    [InlineData("printf 'abcdefgh\\n' | sed -n 'l 4'", "abc\\\ndef\\\ngh$\n")]
    [InlineData("printf 'abcdefgh\\n' | sed -n 'l 0'", "abcdefgh$\n")]
    [InlineData("printf 'a\\nb\\n' | sed -n 'N;l'", "a\\nb$\n")]
    public void ListRendersUnambiguously(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    // Seeded as bytes rather than through printf: these are exactly the escapes printf does not
    // itself interpret, and the subject here is sed's rendering, not printf's reading.
    [Theory]
    [InlineData("a\vb\n", "a\\vb$\n")]
    [InlineData("a\fb\n", "a\\fb$\n")]
    [InlineData("a\u0001b\n", "a\\001b$\n")]
    [InlineData("a\u007fb\n", "a\\177b$\n")]
    public void ListRendersControlCharactersAsOctal(string content, string expected)
    {
        using ShellHarness harness = new();
        harness.Write("control.txt", content);

        Assert.Equal(expected, harness.Run("sed -n 'l' control.txt").Stdout);
    }

    [Fact]
    public void APipelineStopsPullingWhenTheConsumerStops()
    {
        using ShellHarness harness = new();
        CountingApplet counter = new();
        harness.Add(counter);

        Assert.Equal("line 1\n", harness.Run("counting | sed -n '1p;1q'").Stdout);
        Assert.True(counter.Produced < 100, $"produced {counter.Produced} chunks after the consumer stopped");
    }

    [Fact]
    public void ARunawayBranchStopsWhenCancelled()
    {
        using ShellHarness harness = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        ShellExecutor executor = new(AppletRegistry.CreateDefault(), new NotSupportedCommandExecutor());
        ShellResult result = executor.Execute("printf 'a\\n' | sed ':a;ba'", new ShellState(harness.Root), cancellation.Token);

        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("sed --debug 's/a/b/'")]
    [InlineData("sed --sandbox 's/a/b/'")]
    [InlineData("sed --follow-symlinks -i 's/a/b/' f")]
    [InlineData("sed --version")]
    [InlineData("sed --help")]
    [InlineData("sed -Z 's/a/b/'")]
    public void FlagsTheSandboxCannotHonourAreRefused(string commandLine)
    {
        IReadOnlyList<string> arguments = [.. commandLine.Split(' ').Skip(1)];
        Assert.False(new SedApplet().CheckFlags(arguments).IsSupported);
    }

    [Theory]
    [InlineData("sed --line-length=wide 'l'", "sed: invalid line length: wide")]
    [InlineData("sed", "sed: usage: sed [options] script [file...]")]
    [InlineData("sed 's/a'", "sed: -e expression #1: ")]
    public void MalformedInvocationsAreReportedWithExitTwo(string commandLine, string expected)
    {
        ShellResult result = Run(commandLine);

        Assert.Equal(2, result.ExitCode);
        Assert.StartsWith(expected, result.Stderr, StringComparison.Ordinal);
    }

    // The executor refuses these at CheckFlags and never calls Run, so the applet's own refusal
    // arms are reachable only by invoking it directly — which a host embedding Sharp.Shell does.
    [Theory]
    [InlineData("sed: unsupported: ", "--debug", "s/a/b/")]
    [InlineData("sed: unsupported construct: ", "s/a/b/e")]
    public void RunRefusesWhatCheckFlagsWouldHaveCaught(string expected, params string[] arguments)
    {
        using ShellHarness harness = new();
        System.Text.StringBuilder errors = new();

        AppletRun run = new SedApplet().Run(AppletContexts.For(
            arguments,
            TextStream.FromText("a\n"),
            new ShellState(harness.Root),
            text => errors.Append(text)));

        Assert.Equal(2, run.ExitCode);
        Assert.StartsWith(expected, errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AScriptFileThatIsNotThereIsReported()
    {
        using ShellHarness harness = new();
        ShellResult result = harness.Run("sed -f absent.sed in.txt");

        Assert.Equal(2, result.ExitCode);
        Assert.StartsWith("sed: ", result.Stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("e ls")]
    [InlineData("s/a/b/e")]
    [InlineData(@"s/a\|ab/x/")]
    public void ScriptConstructsTheSandboxCannotHonourAreRefused(string script)
    {
        Assert.False(new SedApplet().CheckFlags([script]).IsSupported);
    }

    // --posix turns the GNU regex extensions off, so a script that uses one is handed back whole
    // rather than run against a dialect the flag says is not in play.
    [Theory]
    [InlineData(@"s/\w/x/")]
    [InlineData(@"s/\s/x/")]
    [InlineData(@"s/\<a\>/x/")]
    public void PosixModeRefusesTheGnuRegexEscapes(string script)
    {
        Assert.False(new SedApplet().CheckFlags(["--posix", script]).IsSupported);
        Assert.True(new SedApplet().CheckFlags([script]).IsSupported);
    }

    [Theory]
    [InlineData("sed -n '1,50p' f", false)]
    [InlineData("sed 's/a/b/' f", false)]
    [InlineData("sed -i 's/a/b/' f", true)]
    [InlineData("sed -n '/a/w out.txt' f", true)]
    [InlineData("sed 's/a/b/w out.txt' f", true)]
    public void MutationIsDecidedPerInvocation(string commandLine, bool expected)
    {
        IReadOnlyList<string> arguments = [.. SplitArguments(commandLine).Skip(1)];
        Assert.Equal(expected, new SedApplet().MutatesWith(arguments));
    }

    // Order is load-bearing: the same two scripts in the other order give the other answer.
    [Fact]
    public void ScriptSourcesRunInCommandLineOrder()
    {
        using ShellHarness harness = new();
        harness.Write("first.sed", "s/a/1/\n");
        harness.Write("in.txt", "a\n");

        Assert.Equal("Z\n", harness.Run("sed -f first.sed -e 's/1/Z/' in.txt").Stdout);
        Assert.Equal("1\n", harness.Run("sed -e 's/1/Z/' -f first.sed in.txt").Stdout);
    }

    [Fact]
    public void WriteTargetsExistFromTheMomentTheScriptLoads()
    {
        using ShellHarness harness = new();
        harness.Write("in.txt", "a\n");
        harness.Write("stale.txt", "from an earlier run\n");

        harness.Run("sed -n '/nothing-matches/w created.txt' in.txt");
        harness.Run("sed -n '/nothing-matches/w stale.txt' in.txt");

        Assert.Equal(string.Empty, File.ReadAllText(Path.Combine(harness.Root, "created.txt")));
        Assert.Equal(string.Empty, File.ReadAllText(Path.Combine(harness.Root, "stale.txt")));
    }

    // The newline belongs to the record being printed, not to the last record read: line 1 of an
    // input whose *last* line lacks a newline still ends with one.
    [Theory]
    [InlineData("printf 'a\\nb\\nc' | sed -n '1p'", "a\n")]
    [InlineData("printf 'a\\nb\\nc' | sed -n '1,2p'", "a\nb\n")]
    [InlineData("printf 'a\\nb\\nc' | sed -n '$p'", "c")]
    [InlineData("printf 'a\\nb\\nc' | sed '$p'", "a\nb\ncc")]
    [InlineData("printf 'a\\nb\\nc' | sed -n '='", "1\n2\n3\n")]
    [InlineData("printf 'a\\nb\\nc' | sed -n 'P'", "a\nb\nc")]
    [InlineData("printf 'a\\nb\\nc' | sed '$a END'", "a\nb\ncEND\n")]
    public void TheTrailingNewlineFollowsTheRecordThatIsPrinted(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    // An empty match sitting where the previous match ended is not a second match.
    [Theory]
    [InlineData("printf 'a\\n' | sed 's/a*/X/g'", "X\n")]
    [InlineData("printf 'alpha\\n' | sed 's/a*/X/g'", "XlXpXhX\n")]
    [InlineData("printf 'ab\\n' | sed 's/x*/-/g'", "-a-b-\n")]
    [InlineData("printf 'aaa bbb\\n' | sed 's/b*/X/g'", "XaXaXaX X\n")]
    public void EmptyMatchesDoNotDoubleUpAgainstRealOnes(string commandLine, string expected)
    {
        Assert.Equal(expected, Out(commandLine));
    }

    [Fact]
    public void ConcatenatedFilesKeepTheirRecordBoundaries()
    {
        using ShellHarness harness = new();
        harness.Write("first.txt", "a\nb\nc");
        harness.Write("second.txt", "d\ne\n");

        Assert.Equal("a\nb\nc\nd\ne\n", harness.Run("sed -n 'p' first.txt second.txt").Stdout);
        Assert.Equal("5\n", harness.Run("sed -n '$=' first.txt second.txt").Stdout);
        Assert.Equal("a\nb\nc", harness.Run("sed -n 'p' first.txt").Stdout);
    }

    [Fact]
    public void ChangeOnARangeStillOpenAtEndOfInputPrintsItsText()
    {
        Assert.Equal("CHANGED\n", Out("printf 'x\\n' | sed '1,9c CHANGED'"));
        Assert.Equal("CHANGED\nc\n", Out("printf 'a\\nb\\nc\\n' | sed '1,2c CHANGED'"));
    }

    private static IReadOnlyList<string> SplitArguments(string commandLine)
    {
        List<string> arguments = [];
        bool quoted = false;
        System.Text.StringBuilder current = new();

        foreach (char character in commandLine)
        {
            if (character == '\'')
            {
                quoted = !quoted;
                continue;
            }

            if (character == ' ' && !quoted)
            {
                arguments.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        arguments.Add(current.ToString());
        return arguments;
    }
}