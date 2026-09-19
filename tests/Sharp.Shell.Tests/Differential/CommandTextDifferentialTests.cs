using Xunit;

namespace Sharp.Shell.Tests.Differential;

// The round-trip that matters most, checked against the shell that defines it: feed the text back
// through real bash and it must split into the argv it was made from. A quoting rule that only
// satisfies our own lexer would still be wrong at the host boundary.
public class CommandTextDifferentialTests(ITestOutputHelper output)
{
    // NUL rather than newline as the separator: an argument containing a newline is exactly the
    // case a line-based check would get wrong.
    private const string PrintEachArgument = @"%s\0";

    private static readonly string[][] Corpus =
    [
        ["echo", "hi"],
        ["dotnet", "build", "--no-restore"],
        ["dotnet", "build", "my proj"],
        ["echo", ""],
        ["echo", "it's"],
        ["echo", "a \"quote\"", "a 'quote'"],
        ["echo", "$HOME", "${HOME}", "`id`", "$(id)"],
        ["echo", "a\nb", "tab\there"],
        ["echo", "*.cs", "?", "[abc]", "{a,b}"],
        ["echo", "~", "~root", "!", "#", "&", "|", ";", "<", ">", "(", ")"],
        ["echo", "café", "日本語", "naïve"],
        ["echo", @"back\slash", "'", @"\'", "''"],
        ["grep", "-rn", "TODO|FIXME", "src/Sharp.Shell"],
        ["sed", "-i", "s/a b/c d/g", "my file.txt"],
    ];

    [Fact]
    public void RealBashSplitsTheTextBackIntoTheArgv()
    {
        Assert.SkipUnless(BashOracle.IsLive, "no system bash to compare against");
        output.WriteLine($"bash {BashOracle.Version} at {BashOracle.Path}");

        foreach (string[] argv in Corpus)
        {
            output.WriteLine(CommandText.Of(argv[0], argv[1..]));

            Assert.Equal(argv, SplitByRealBash(argv));
        }
    }

    private static IReadOnlyList<string> SplitByRealBash(string[] argv)
    {
        string printing = CommandText.Of("printf", [PrintEachArgument, .. argv]);
        OracleResult result = BashOracle.Run(printing, Path.GetTempPath());

        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(0, result.ExitCode);

        return result.Stdout.Split('\0')[..^1];
    }
}
