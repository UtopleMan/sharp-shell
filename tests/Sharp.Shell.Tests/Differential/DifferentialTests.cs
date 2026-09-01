using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests.Differential;

// Layer 2 of the testing plan: the same command through real bash and through this shell, compared
// on stdout and exit status. Where Layer 1's corpus says what bash *should* do, this says what the
// bash on this machine actually does.
//
// Deviations live in corpus/differential-expected-failures.txt as a two-way ratchet, exactly like
// the conformance list: an unlisted mismatch fails, and so does a listed case that now matches.
public class DifferentialTests(ITestOutputHelper output)
{
    [Fact]
    public void TheLanguageCorpusMatchesRealBash()
    {
        Assert.SkipUnless(BashOracle.IsAvailable, "no system bash to compare against");
        output.WriteLine($"bash {BashOracle.Version} at {BashOracle.Path}");

        List<Mismatch> mismatches = [];

        foreach (string command in LanguageCorpus.Commands)
        {
            if (Compare(command) is { } mismatch)
            {
                mismatches.Add(mismatch);
            }
        }

        int matched = LanguageCorpus.Commands.Count - mismatches.Count;
        output.WriteLine($"{matched}/{LanguageCorpus.Commands.Count} language-corpus commands match");

        if (DifferentialExpectations.IsRegenerating)
        {
            DifferentialExpectations.Write(mismatches, LanguageCorpus.Commands.Count);
            return;
        }

        AssertRatchet(mismatches);
    }

    private void AssertRatchet(IReadOnlyList<Mismatch> mismatches)
    {
        IReadOnlySet<string> expected = DifferentialExpectations.Load();

        string[] unlisted =
        [
            .. mismatches
                .Where(mismatch => !expected.Contains(DifferentialExpectations.Key(mismatch.Command)))
                .Select(mismatch => $"  {mismatch.Command}\n      {mismatch.Detail}"),
        ];

        HashSet<string> mismatchedKeys =
            [.. mismatches.Select(mismatch => DifferentialExpectations.Key(mismatch.Command))];

        string[] unexpectedMatches =
        [
            .. LanguageCorpus.Commands
                .Where(command => expected.Contains(DifferentialExpectations.Key(command)))
                .Where(command => !mismatchedKeys.Contains(DifferentialExpectations.Key(command)))
                .Select(command => $"  {command.Replace("\n", " ", StringComparison.Ordinal)}"),
        ];

        Assert.True(
            unlisted.Length == 0,
            $"{unlisted.Length} command(s) differ from bash without being listed:\n{string.Join('\n', unlisted)}");

        Assert.True(
            unexpectedMatches.Length == 0,
            $"{unexpectedMatches.Length} listed command(s) now match bash — delete their lines:\n{string.Join('\n', unexpectedMatches)}");
    }

    private static Mismatch? Compare(string command)
    {
        using ShellHarness harness = new();
        OracleResult oracle = BashOracle.Run(command, harness.Root);

        using ShellHarness ours = new();
        ShellResult mine = ours.Run(command);

        if (!string.Equals(oracle.Stdout, mine.Stdout, StringComparison.Ordinal))
        {
            return new Mismatch(command, $"stdout {Show(oracle.Stdout)} != {Show(mine.Stdout)}");
        }

        return oracle.ExitCode == mine.ExitCode
            ? null
            : new Mismatch(command, $"status {oracle.ExitCode} != {mine.ExitCode}");
    }

    private static string Show(string text) =>
        $"'{text.Replace("\n", "\\n", StringComparison.Ordinal)}'";
}

public sealed record Mismatch(string Command, string Detail);

// Keyed by a hash of the command, with the command itself written beside it as a comment.
//
// Keying on the raw text was tried and is wrong: several corpus commands contain a literal
// backslash-n inside single quotes (`printf 'a\nb\n'`), so any scheme that escapes real newlines
// as \n cannot tell the two apart and silently corrupts the key. A hash is unambiguous and
// order-independent, and the comment keeps the file readable.
public static class DifferentialExpectations
{
    private const string RelativePath = "tests/Sharp.Shell.Tests/corpus/differential-expected-failures.txt";

    public static bool IsRegenerating =>
        Environment.GetEnvironmentVariable("DUETUI_DIFFERENTIAL_BASELINE") == "1";

    public static IReadOnlySet<string> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "corpus", "differential-expected-failures.txt");

        if (!File.Exists(path))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return File
            .ReadAllLines(path)
            .Select(line => line.Split("  #", 2)[0].Trim())
            .Where(key => key.Length > 0 && !key.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);
    }

    public static void Write(IReadOnlyList<Mismatch> mismatches, int total)
    {
        string? path = SourcePath();
        if (path is null)
        {
            return;
        }

        List<string> lines =
        [
            "# Language-corpus commands where this shell differs from the system bash.",
            "#",
            "# Two-way ratchet: an unlisted difference fails the suite, and so does a listed command",
            "# that now matches — delete its line when that happens. Regenerate with",
            "# DUETUI_DIFFERENTIAL_BASELINE=1 dotnet test tests/Sharp.Shell.Tests/Sharp.Shell.Tests.csproj",
            "#",
            $"# Recorded against bash {BashOracle.Version} on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}.",
            "#",
            "# Some entries are coreutils formatting, not shell-language differences: BSD wc and",
            "# uniq -c pad their counts into columns and the GNU ones do not, and this shell follows",
            "# GNU. On a platform with the other coreutils those lines will report as 'now matching'.",
            "# Regenerate on the platform CI runs on; do not delete a case to get a green board.",
            $"# {mismatches.Count} of {total} commands differ at this baseline.",
            "#",
            "# Each entry is <hash>  # <command> -> <difference>. The hash keys the ratchet; the",
            "# command is there so the file can be read.",
            string.Empty,
        ];

        lines.AddRange(mismatches
            .OrderBy(mismatch => mismatch.Command, StringComparer.Ordinal)
            .Select(mismatch => $"{Key(mismatch.Command)}  # {OneLine(mismatch.Command)} -> {mismatch.Detail}"));
        File.WriteAllLines(path, lines);
    }

    public static string Key(string command) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(command)))[..12];

    private static string OneLine(string command) =>
        command.Replace("\n", "\u23ce", StringComparison.Ordinal);

    private static string? SourcePath()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, RelativePath);
            if (Directory.Exists(Path.GetDirectoryName(candidate)!))
            {
                return candidate;
            }
        }

        return null;
    }
}
