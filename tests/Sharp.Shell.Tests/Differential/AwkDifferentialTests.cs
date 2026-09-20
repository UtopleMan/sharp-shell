using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests.Differential;

public sealed record AwkCase(string Input, params string[] Arguments);

// Our awk against two others: one-true-awk, which this dialect targets, and `gawk --posix`, which is
// an independent implementation of the same standard. A case only counts when both agree with each
// other, so "matches an awk" cannot be mistaken for "matches awk".
//
// Every case here comes out of a sweep of about 860 program shapes against both oracles — 370
// written by hand and 494 generated from a pattern-by-subject matrix, roughly 14 700 comparisons in
// all. Every construct the two oracles disagree on is excluded by construction and written up in
// corpus/awk-dialect-buckets.md instead.
public class AwkDifferentialTests(ITestOutputHelper output)
{
    private const string Words = "a b c\n";

    private const string Rows = "1 2 3\n4 5 6\n";

    private const string Lines = "one\ntwo\nthree\nfour\nfive\n";

    private const string Colons = "a:b:c\nd:e:f\n";

    private const string Ragged = "  leading   and  trailing  \n";

    private const string Numbers = "10 9\n9 10\n";

    private const string Paragraphs = "a\n\nb\nc\n\n\nd\n";

    private const string Unterminated = "abc";

    public static readonly AwkCase[] Cases =
    [
        // Records and fields.
        new(Words, "{print}"),
        new(Words, "{print $0}"),
        new(Words, "{print $1}"),
        new(Words, "{print $2}"),
        new(Words, "{print $NF}"),
        new(Words, "{print $(NF-1)}"),
        new(Words, "{print NF}"),
        new(Ragged, "{print NF, $1, $2}"),
        new(Unterminated, "{print NF, $1}"),
        new(Rows, "{print NR, $0}"),
        new(Rows, "{print FNR}"),
        new(Lines, "END {print NR}"),
        new(Lines, "END {print}"),
        new(Lines, "BEGIN {print \"begin\"} {print} END {print \"end\"}"),
        new(Words, "{print $-0}"),
        new(Words, "{print $NF, $(NF-0)}"),

        // Patterns.
        new(Lines, "NR == 1"),
        new(Lines, "NR > 1"),
        new(Lines, "NR >= 2 && NR <= 3"),
        new(Words, "$1 == \"a\""),
        new(Words, "$1 != \"a\""),
        new(Numbers, "$1 < $2"),
        new(Numbers, "$1 > $2"),
        new(Numbers, "$1 <= $2"),
        new(Numbers, "$1 >= $2"),
        new(Rows, "$1 == 1"),
        new(Lines, "/o/"),
        new(Lines, "!/o/"),
        new(Words, "/a/ && /b/"),
        new(Words, "/a/ || /z/"),
        new(Words, "$0 ~ /a/"),
        new(Words, "$0 !~ /a/"),
        new(Words, "$1 ~ /^a/"),
        new(Lines, "/one/, /three/"),
        new(Lines, "NR == 1, NR == 2"),
        new(Lines, "/two/, /two/"),

        // Field assignment and rebuilding.
        new(Words, "{$1 = \"Z\"; print}"),
        new(Words, "{$2 = \"Z\"; print}"),
        new(Words, "{$(NF + 1) = \"new\"; print}"),
        new(Words, "{NF = 1; print}"),
        new(Words, "{NF = 5; print NF, $0 \"|\"}"),
        new(Words, "{$0 = toupper($0); print NF, $1}"),
        new(Words, "{$1 = \"\"; print \"[\" $0 \"]\"}"),
        new(Words, "{$1 = $1; print}"),
        new(Words, "{for (i = 1; i <= NF; i++) $i = toupper($i); print}"),
        new(Words, "{$0 = $1; print NF, $0}"),
        new(Words, "{n = NF; $0 = $0 \" extra\"; print n, NF}"),

        // Separators.
        new(Colons, "-F:", "{print $2}"),
        new(Colons, "-F:", "{print NF}"),
        new(Colons, "BEGIN {FS = \":\"} {print $2}"),
        new(Colons, "BEGIN {FS = \"[:,]\"} {print NF}"),
        new(Colons, "BEGIN {FS = \"(:|,)\"} {print NF}"),
        new(Rows, "BEGIN {FS = \"[0-9]\"} {print NF}"),
        new(Colons, "BEGIN {FS = \":\"; OFS = \"|\"} {$1 = $1; print}"),
        new(Words, "BEGIN {OFS = \"-\"} {$1 = $1; print}"),
        new(Words, "BEGIN {OFS = \"-\"} {print $1, $2}"),
        new(Words, "BEGIN {ORS = \"|\"} {print}"),
        new(Colons, "{FS = \":\"; print $1}"),
        new(Paragraphs, "BEGIN {RS = \"\"} {print NR, NF}"),
        new(Paragraphs, "BEGIN {RS = \"\"} {print $1, $NF}"),
        new(Paragraphs, "BEGIN {RS = \"\"; FS = \"\\n\"} {print NF}"),
        new(Paragraphs, "BEGIN {RS = \"\"} END {print NR}"),
        new(Colons, "BEGIN {RS = \":\"} {print NR, \"[\" $0 \"]\"}"),
        new(Words, "BEGIN {RS = \"b\"} {print NR, \"[\" $0 \"]\"}"),

        // Arithmetic and the value model.
        new(Words, "BEGIN {print 1/3}"),
        new(Words, "BEGIN {print 2^53}"),
        new(Words, "BEGIN {print 1e6}"),
        new(Words, "BEGIN {print 100000}"),
        new(Words, "BEGIN {print 0.0000001}"),
        new(Words, "BEGIN {print 0.1 + 0.2}"),
        new(Words, "BEGIN {print 3 / 2, 4 / 2}"),
        new(Words, "BEGIN {print 2 ^ 10, 2 ^ 0.5}"),
        new(Words, "BEGIN {print -2 ^ 2}"),
        new(Words, "BEGIN {print 7 % 3, -7 % 3, 7 % -3}"),
        new(Words, "BEGIN {print (\"abc\" < \"abd\"), (\"abc\" < \"ab\")}"),
        new(Words, "BEGIN {x = \"3x\"; print x + 1}"),
        new(Words, "BEGIN {x = \"  42  \"; print x + 1}"),
        new(Words, "BEGIN {print (\"\" == 0), (\"\" == \"\")}"),
        new(Numbers, "{print ($1 < 5), ($1 < \"5\")}"),
        new(Numbers, "{print $1 + $2, $1 $2}"),
        new(Words, "BEGIN {CONVFMT = \"%.2g\"; print (1/3) \"\"}"),
        new(Words, "BEGIN {OFMT = \"%.2g\"; print 1/3}"),
        new(Words, "BEGIN {print 1 \" \" 2}"),
        new(Words, "BEGIN {print 1 -1}"),
        new(Words, "BEGIN {print 1 \" \" -1}"),
        new(Words, "BEGIN {print \"a\" \"b\" \"c\"}"),
        new(Words, "BEGIN {x = 1; x += 2; x -= 1; x *= 3; x /= 2; x %= 4; x ^= 2; print x}"),
        new(Words, "BEGIN {i = 1; print i++, i, ++i, i}"),
        new(Words, "BEGIN {i = 1; print i--, i, --i, i}"),
        new(Rows, "{print $1++, $1}"),
        new(Rows, "{print ++$1, $1}"),
        new(Words, "BEGIN {print 1 && 0, 1 || 0, !1, !0, !\"\"}"),

        // String builtins.
        new(Words, "{print length($0)}"),
        new(Words, "{print length}"),
        new(Words, "{print substr($0, 2)}"),
        new(Words, "{print substr($0, 2, 3)}"),
        new(Words, "{print substr($0, 0, 2)}"),
        new(Words, "{print substr($0, -1, 3)}"),
        new(Words, "{print toupper($0)}"),
        new(Words, "{print tolower($0)}"),
        new(Words, "{n = split($0, p); print n, p[1]}"),
        new(Colons, "{n = split($0, p, \":\"); print n, p[2]}"),
        new(Colons, "{n = split($0, p, /[ ,:]/); print n, p[1]}"),
        new(Words, "{gsub(/a/, \"X\"); print}"),
        new(Words, "{gsub(/[aeiou]/, \"-\"); print}"),
        new(Words, "{sub(/^/, \">\"); print}"),
        new(Words, "{sub(/$/, \"<\"); print}"),
        new(Words, "{print gsub(/a/, \"b\"), $0}"),
        new(Rows, "{if (match($0, /[0-9]+/)) print RSTART, RLENGTH; else print \"none\"}"),
        new(Words, "BEGIN {s = \"aaa\"; print gsub(/a/, \"b\", s), s}"),
        new(Words, "BEGIN {s = \"ab\"; print gsub(/a*/, \"-\", s), s}"),
        new(Words, "BEGIN {s = \"abc\"; print gsub(/x*/, \"-\", s), s}"),
        new(Words, "BEGIN {s = \"hello\"; print sub(/l/, \"[&]\", s), s}"),
        new(Words, "BEGIN {s = \"hello\"; print sub(/l/, \"[\\\\&]\", s), s}"),
        new(Words, "BEGIN {s = \"a1b2c3\"; print gsub(/[0-9]/, \"<&>\", s), s}"),
        new(Words, "BEGIN {s = \"  x  \"; print gsub(/^ +| +$/, \"\", s), \"[\" s \"]\"}"),
        new(Words, "BEGIN {print length(\"\")}"),
        new(Words, "BEGIN {print substr(\"hello\", 2, 0) \"|\"}"),
        new(Words, "BEGIN {s = \"abc\"; print gsub(//, \"-\", s), s}"),
        new(Words, "BEGIN {print sprintf(\"%03d\", 7)}"),

        // Leftmost-longest, the part sed has to refuse.
        new(Words, "BEGIN {print match(\"xfoobar\", \"foo|foobar\"), RSTART, RLENGTH}"),
        new(Words, "BEGIN {print match(\"ababa\", \"(a|ab)+\"), RSTART, RLENGTH}"),
        new(Words, "BEGIN {print match(\"abxbcde\", \"b|bcde\"), RSTART, RLENGTH}"),
        new(Words, "BEGIN {s = \"ababab\"; print gsub(/a|ab/, \"-\", s), s}"),
        new(Words, "BEGIN {s = \"aab\"; print gsub(/a|aa/, \"X\", s), s}"),
        new(Words, "BEGIN {n = split(\"aXbXXc\", p, \"X|XX\"); print n, p[1], p[2], p[3]}"),
        new(Words, "BEGIN {print match(\"aXbXc\", /X.*X/), RSTART, RLENGTH}"),
        new(Words, "BEGIN {print match(\"aXbXc\", /X[^X]*X/), RSTART, RLENGTH}"),
        new(Words, "BEGIN {print match(\"abc\", /^abc$/), RSTART, RLENGTH}"),
        new(Words, "BEGIN {print (\"A\" ~ /[[:upper:]]/), (\"a\" ~ /[[:upper:]]/)}"),

        // printf.
        new(Words, "{printf \"%s\\n\", $1}"),
        new(Rows, "{printf \"%d\\n\", $1}"),
        new(Rows, "{printf \"%5.2f\\n\", $1}"),
        new(Words, "{printf \"%-10s|\\n\", $1}"),
        new(Words, "{printf \"%s-%s\\n\", $1, $2}"),
        new(Words, "BEGIN {printf \"%c%c%c\\n\", 104, 105, 33}"),
        new(Words, "BEGIN {printf \"%i %u %o %x %X\\n\", 42, 42, 42, 42, 42}"),
        new(Words, "BEGIN {printf \"%*d|%-*d|\\n\", 6, 7, 6, 7}"),
        new(Words, "BEGIN {printf \"%.0e %.0f %.0g\\n\", 12345, 12345, 12345}"),
        new(Words, "BEGIN {printf \"%+.2f % .2f\\n\", 1.5, 1.5}"),
        new(Words, "BEGIN {printf \"[%5s][%-5s]\\n\", \"ab\", \"ab\"}"),
        new(Words, "BEGIN {printf \"[%.2s]\\n\", \"abcdef\"}"),
        new(Words, "BEGIN {printf \"[%5.2f][%-8.3e]\\n\", 3.14159, 314.159}"),
        new(Words, "BEGIN {printf \"[%d][%d][%d]\\n\", \"12abc\", \" 7 \", \"abc\"}"),
        new(Words, "BEGIN {printf \"%s\\n\", 1000000}"),
        new(Words, "BEGIN {printf \"%s%s%s\\n\", 1, \"\", 2}"),

        // Arrays.
        new(Lines, "{a[NR] = $0} END {for (i = 1; i <= NR; i++) print a[i]}"),
        new(Lines, "{a[$1]++} END {n = 0; for (k in a) n++; print n}"),
        new(Words, "BEGIN {a[\"x\"] = 1; if (\"x\" in a) print \"yes\"}"),
        new(Words, "BEGIN {a[1, 2] = 3; if ((1, 2) in a) print \"yes\"}"),
        new(Words, "BEGIN {a[1] = 1; delete a[1]; print length(a)}"),
        new(Words, "BEGIN {a[\"1\"] = \"x\"; print a[1]}"),
        new(Words, "BEGIN {a[1] = \"x\"; print a[\"1\"]}"),
        new(Words, "BEGIN {n = split(\"\", p); print n, length(p)}"),
        new(Words, "BEGIN {a[1]; print length(a), (1 in a)}"),
        new(Words, "BEGIN {x = a[1]; print length(a)}"),
        new(Lines, "!seen[$0]++"),
        new(Rows, "{sum[$1] += $2} END {n = 0; for (k in sum) n += sum[k]; print n}"),

        // Control flow.
        new(Words, "{for (i = 1; i <= NF; i++) printf \"%s|\", $i; printf \"\\n\"}"),
        new(Words, "{i = 0; while (i < NF) {i++; printf \"%s.\", $i}; printf \"\\n\"}"),
        new(Words, "{i = 0; do {i++} while (i < 2); print i}"),
        new(Words, "{for (i = NF; i >= 1; i--) printf \"%s%s\", $i, (i > 1 ? \" \" : \"\\n\")}"),
        new(Words, "{if (NF > 2) print \"wide\"; else print \"narrow\"}"),
        new(Lines, "{if (NR == 1) next; print}"),
        new(Lines, "{if (NR == 1) {print \"one\"} else if (NR == 2) {print \"two\"} else {print \"more\"}}"),
        new(Lines, "{next} END {print NR}"),
        new(Lines, "{exit} END {print \"end\", NR}"),
        new(Lines, "NR == 2 {exit 3} {print}"),
        new(Words, "BEGIN {exit 1}"),
        new(Words, "BEGIN {while (i++ < 3) print i}"),
        new(Words, "BEGIN {for (;;) {n++; if (n > 3) break}; print n}"),
        new(Words, "BEGIN {for (i = 0; i < 5; i++) {if (i == 2) continue; printf \"%d\", i}; print \"\"}"),

        // User functions.
        new(Words, "function f(a, b) {return a + b} BEGIN {print f(1, 2)}"),
        new(Words, "function fact(n) {return n < 2 ? 1 : n * fact(n - 1)} BEGIN {print fact(6)}"),
        new(Words, "function fill(a) {a[\"k\"] = 1} BEGIN {fill(x); print length(x)}"),
        new(Words, "function head(s, n) {return substr(s, 1, n)} {print head($0, 3)}"),
        new(Words, "function f(a) {a = a + 1; return a} BEGIN {x = 1; print f(x), x}"),
        new(Words, "function r(n) {if (n <= 0) return \"\"; return \"*\" r(n - 1)} BEGIN {print r(5)}"),
        new(Numbers, "function m(a, b) {return a > b ? a : b} {print m($1, $2)}"),
        new(Words, "function noret(x) {x = 1} BEGIN {print noret(1) \"|\"}"),
        new(Words, "function deflt(a, b) {return b} BEGIN {print deflt(1) \"|\"}"),
        new(Words, "function add(a, b) {return a + b} function twice(x) {return add(x, x)} BEGIN {print twice(21)}"),

        // Command line.
        new(Colons, "-F:", "-v", "n=2", "{print $n}"),
        new(Colons, "-v", "FS=:", "{print $2}"),
        new(Words, "-v", "OFS=:", "{$1 = $1; print}"),
        new(Words, "-v", "ORS=:", "{print}"),
        new(Words, "-v", "x=5", "BEGIN {print x}"),
        new(Words, "-v", "x=5", "{print x + NR}"),
        new(Words, "-v", "CONVFMT=%.2g", "BEGIN {print (1/3) \"\"}"),
        new(Words, "-v", "OFMT=%.2g", "BEGIN {print 1/3}"),
        new(Words, "-v", "SUBSEP=:", "BEGIN {a[1, 2] = 1; for (k in a) print k}"),
        new(Words, "-v", "x=a\\tb", "BEGIN {print length(x)}"),
        new(Colons, "-F", "[,:]", "{print NF}"),

        // Output to the applet's own streams.
        new(Words, "{print > \"/dev/stdout\"}"),
        new(Words, "{printf \"%s\\n\", $0 > \"/dev/stdout\"}"),
    ];

    public static TheoryData<int> CaseIndices => [.. Enumerable.Range(0, Cases.Length)];

    [Theory]
    [MemberData(nameof(CaseIndices))]
    public void OurAwkAgreesWithBothOracles(int caseIndex)
    {
        Assert.SkipUnless(AwkOracle.IsAvailable, "one of the awk oracles is missing on this machine");

        AwkCase awkCase = Cases[caseIndex];
        string root = Directory.CreateTempSubdirectory("duetui-awk-differential").FullName;

        try
        {
            output.WriteLine($"awk {string.Join(' ', awkCase.Arguments)} <<< {Escape(awkCase.Input)}");
            (string actual, int exitCode) = RunOurs(awkCase, root);

            foreach (ExternalOracle oracle in AwkOracle.All)
            {
                OracleResult expected = oracle.Run(awkCase.Arguments, awkCase.Input, root);

                Assert.Equal(expected.Stdout, actual);
                Assert.Equal(Failed(expected.ExitCode), Failed(exitCode));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // A case using a construct or a flag this dialect refuses would compare our escalation against an
    // oracle's answer, and the mismatch would look like a bug rather than a scope decision. Asked of
    // the applet rather than by substring search, so `/a/ || /z/` is not mistaken for a pipeline.
    [Fact]
    public void OnlyConstructsThisDialectImplementsAreCompared()
    {
        foreach (AwkCase awkCase in Cases)
        {
            FlagSupport support = new AwkApplet("awk").CheckFlags(awkCase.Arguments);

            Assert.True(support.IsSupported, $"{support.UnsupportedFlag} in: {string.Join(' ', awkCase.Arguments)}");
        }
    }

    // `for (k in a)` visits an array in an unspecified order — the sweep that produced this list found
    // exactly one disagreement across ~14 700 comparisons, and it was iteration order. So the cases
    // that iterate are enumerated here: each computes something order-insensitive, and adding another
    // one forces that judgement to be made again rather than assumed.
    [Fact]
    public void EveryIteratingCaseIsOrderInsensitive()
    {
        string[] iterating =
        [
            "{a[$1]++} END {n = 0; for (k in a) n++; print n}",
            "{sum[$1] += $2} END {n = 0; for (k in sum) n += sum[k]; print n}",
            "BEGIN {a[1, 2] = 1; for (k in a) print k}",
        ];

        List<string> found =
        [
            .. Cases
                .SelectMany(awkCase => awkCase.Arguments)
                .Where(argument => argument.Contains("for (", StringComparison.Ordinal)
                    && argument.Contains(" in ", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal),
        ];

        Assert.Equal([.. iterating.Order(StringComparer.Ordinal)], [.. found.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void TheOraclesAreReported()
    {
        foreach (ExternalOracle oracle in AwkOracle.All)
        {
            output.WriteLine($"{oracle.Name}: {oracle.Path ?? "unavailable"} — {oracle.Version}");
        }

        output.WriteLine($"cases compared: {Cases.Length}");
    }

    // The oracles report different exit codes for the same failure — one-true-awk and gawk both use
    // 2, but a program's own `exit 3` has to match exactly while a diagnostic's status only has to
    // agree about having failed.
    private static int Failed(int exitCode) => exitCode is 0 or 1 or 2 or 3 ? exitCode : 1;

    private static (string Stdout, int ExitCode) RunOurs(AwkCase awkCase, string root)
    {
        AppletRun run = new AwkApplet("awk").Run(AppletContexts.For(
            awkCase.Arguments,
            TextStream.FromText(awkCase.Input),
            new ShellState(root),
            _ => { }));

        return (TextStream.Collect(run.Output), run.ExitCode);
    }

    private static string Escape(string text) => text.Replace("\n", "\\n", StringComparison.Ordinal);
}
