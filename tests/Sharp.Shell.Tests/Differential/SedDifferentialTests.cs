using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
using Xunit;

namespace Sharp.Shell.Tests.Differential;

public sealed record SedCase(string Input, params string[] Arguments);

// Our sed against the system sed, on scripts both BSD and GNU implement identically. The whole class
// skips where no sed exists, which is every Windows agent.
public class SedDifferentialTests(ITestOutputHelper output)
{
    private const string ThreeLines = "a\nb\nc\n";

    private const string Numbered = "one\ntwo\nthree\nfour\nfive\n";

    private const string Repeated = "aaa bbb aaa\n";

    // Every entry must be valid *and identical* in both dialects; PosixOnlyConstructsAreUsed guards
    // the syntax, and two behavioural traps are avoided by construction: `N` on the last line prints
    // the pattern space in GNU and discards it in POSIX, and BSD reads a label as running to the end
    // of the line, so a branch script is spelled with one -e per command.
    public static readonly SedCase[] Cases =
    [
        new(ThreeLines, "-n", "1p"),
        new(ThreeLines, "-n", "2p"),
        new(ThreeLines, "-n", "$p"),
        new(ThreeLines, "-n", "1,2p"),
        new(ThreeLines, "-n", "2,$p"),
        new(Numbered, "-n", "2,4p"),
        new(Numbered, "-n", "1p;4p"),
        new(Numbered, "-n", "/two/p"),
        new(Numbered, "-n", "/two/,/four/p"),
        new(Numbered, "-n", "/nothing/p"),
        new(ThreeLines, "-n", "/b/!p"),
        new(ThreeLines, "1d"),
        new(ThreeLines, "$d"),
        new(ThreeLines, "2,3d"),
        new(ThreeLines, "/b/d"),
        new(Numbered, "2q"),
        new(Numbered, "-n", "3{p;}"),
        new(Numbered, "-n", "/two/{p;p;}"),
        new(Repeated, "s/aaa/x/"),
        new(Repeated, "s/aaa/x/g"),
        new(Repeated, "s/aaa/x/2"),
        new(Repeated, "s/a*/x/"),
        new(Repeated, "s/[ab]*/x/"),
        new(Repeated, "s/\\(a*\\) \\(b*\\)/\\2 \\1/"),
        new(Repeated, "s/aaa/[&]/g"),
        new(Repeated, "s/aaa/&&/"),
        new(Repeated, "s|aaa|x|"),
        new(Repeated, "s,aaa,x,"),
        new("a.b\n", "s/a\\.b/x/"),
        new("a.b\n", "s/a.b/x/"),
        new("aXb\n", "s/a[.]b/x/"),
        new("a/b\n", "s/a\\/b/x/"),
        new("x123y\n", "s/[0-9][0-9]*/N/"),
        new("x123y\n", "s/[[:digit:]][[:digit:]]*/N/"),
        new("hello world\n", "s/[[:alpha:]]*/X/"),
        new("hello world\n", "s/o/0/g"),
        new("hello\n", "s/^h/H/"),
        new("hello\n", "s/o$/O/"),
        new("hello\n", "s/^hello$/x/"),
        new("a+b\n", "s/a+b/x/"),
        new("aab\n", "s/a\\{2\\}b/x/"),
        new("abc\n", "y/abc/xyz/"),
        new(ThreeLines, "-n", "$="),
        new(ThreeLines, "="),
        new(ThreeLines, "-n", "1!G;h;$p"),
        new(ThreeLines, "-n", "H;${x;s/\\n/,/g;p;}"),
        new("a\nb\n", "N;s/\\n/-/"),
        new(ThreeLines, "-n", "N;P;D"),
        new(ThreeLines, "$!N;s/\\n/+/"),
        new("aaa\n", "-e", ":a", "-e", "s/a/b/", "-e", "ta"),
        new(ThreeLines, "-e", "1d", "-e", "$d"),
        new(ThreeLines, "-n", "-e", "1p", "-e", "3p"),
        new("a\nb", "-n", "p"),
        new("a\nb", "-n", "$p"),
        new("", "-n", "p"),
        new("only\n", "s/only/replaced/"),
        new("tab\there\n", "s/\\t/ /"),
        new(Numbered, "-n", "2,3!p"),
        new(Numbered, "3,1p"),
        new("Mixed CASE\n", "s/[[:upper:]]/_/g"),
        new("a b  c\n", "s/  */ /g"),
        new("### x\n", "s/#*//"),
        new(ThreeLines, "-n", "/a/,/b/{/b/p;}"),
        new(Numbered, "2i\\\ninserted"),
        new(Numbered, "2a\\\nappended"),
        new(Numbered, "2c\\\nchanged"),

        // Regressions found by sweeping this applet against the system sed after it was written:
        // where the trailing newline goes, empty matches next to real ones, and a range that is
        // still open when the input ends.
        new("a\nb\nc", "-n", "1p"),
        new("a\nb\nc", "-n", "1,2p"),
        new("a\nb\nc", "-n", "$p"),
        new("a\nb\nc", "$p"),
        new("a\nb\nc", "-n", "="),
        new("a\nb\nc", "-n", "P"),
        new("a\nb\nc", "$d"),
        new("a\n", "s/a*/X/g"),
        new("alpha\n", "s/a*/X/g"),
        new("ab\n", "s/x*/-/g"),
        new("aaa bbb\n", "s/b*/X/g"),
        new("aaa\n", "s/a*/X/g"),
        new("x\n", "1,9c\\\nCHANGED"),
        new(ThreeLines, "1,9c\\\nCHANGED"),
    ];

    public static TheoryData<int> CaseIndices => [.. Enumerable.Range(0, Cases.Length)];

    [Theory]
    [MemberData(nameof(CaseIndices))]
    public void OurSedAgreesWithTheSystemSed(int caseIndex)
    {
        Assert.SkipUnless(SedOracle.IsAvailable, "no system sed on this machine");

        SedCase sedCase = Cases[caseIndex];
        string root = Directory.CreateTempSubdirectory("duetui-sed-differential").FullName;

        try
        {
            OracleResult expected = SedOracle.Run(sedCase.Arguments, sedCase.Input, root);
            (string actual, int exitCode) = RunOurs(sedCase, root);

            output.WriteLine($"sed {string.Join(' ', sedCase.Arguments)} <<< {Escape(sedCase.Input)}");
            Assert.Equal(expected.Stdout, actual);
            Assert.Equal(expected.ExitCode, exitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // A GNU-only construct here would compare our answer against a dialect the oracle does not
    // implement, and the mismatch would look like our bug.
    [Fact]
    public void PosixOnlyConstructsAreUsed()
    {
        string[] gnuOnly = ["\\+", "\\?", "\\|", "~", "0,/", "\\w", "\\s", "\\b", "\\<", "\\>"];
        string[] gnuOnlyFlags = ["-r", "-E", "-s", "-z", "-i", "--posix", "--regexp-extended", "-u"];

        foreach (SedCase sedCase in Cases)
        {
            foreach (string argument in sedCase.Arguments)
            {
                Assert.DoesNotContain(argument, gnuOnlyFlags);
                Assert.All(gnuOnly, construct => Assert.DoesNotContain(construct, argument, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void TheOracleIsReported()
    {
        output.WriteLine($"sed: {SedOracle.Path ?? "unavailable"} — {SedOracle.Version}");
        output.WriteLine($"cases compared: {Cases.Length}");
    }

    private static (string Stdout, int ExitCode) RunOurs(SedCase sedCase, string root)
    {
        AppletRun run = new SedApplet().Run(new AppletContext(
            sedCase.Arguments,
            TextStream.FromText(sedCase.Input),
            new ShellState(root),
            _ => { },
            CancellationToken.None));

        string stdout = TextStream.Collect(run.Output);
        return (stdout, run.ExitCode);
    }

    private static string Escape(string text) => text.Replace("\n", "\\n", StringComparison.Ordinal);
}
