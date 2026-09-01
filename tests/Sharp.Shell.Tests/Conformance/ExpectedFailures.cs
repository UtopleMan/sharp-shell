namespace Sharp.Shell.Tests.Conformance;

// The deviations list, keyed by case id. It is a two-way ratchet: CI fails if an unlisted case
// fails, and also fails if a listed case starts passing. Without the second direction the file
// rots into stale excuses that hide regressions.
//
// The vendored corpus is never edited to record a deviation — that would make every upstream
// re-sync a merge conflict. Deviations live here instead.
public static class ExpectedFailures
{
    private const string RelativePath = "tests/Sharp.Shell.Tests/corpus/expected-failures.txt";

    public static IReadOnlySet<string> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "corpus", "expected-failures.txt");

        if (!File.Exists(path))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return File
            .ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split([' ', '\t'], 2)[0])
            .ToHashSet(StringComparer.Ordinal);
    }

    // Set DUETUI_CORPUS_BASELINE=1 to rewrite the list from the current run. Use it when the
    // corpus is re-synced or when a phase deliberately changes what passes — never to make a
    // regression go away.
    public static bool IsRegenerating =>
        Environment.GetEnvironmentVariable("DUETUI_CORPUS_BASELINE") == "1";

    public static string? SourcePath()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, RelativePath);
            if (File.Exists(candidate) || Directory.Exists(Path.GetDirectoryName(candidate)!))
            {
                return candidate;
            }
        }

        return null;
    }

    public static void Write(string path, IReadOnlyList<CaseOutcome> failures, int total)
    {
        List<string> lines =
        [
            "# Cases from the vendored oils corpus that this shell does not pass.",
            "#",
            "# Two-way ratchet: an unlisted failure fails the suite, and so does a listed case that",
            "# now passes — delete its line when that happens. Regenerate with",
            "# DUETUI_CORPUS_BASELINE=1 dotnet test tests/Sharp.Shell.Tests/Sharp.Shell.Tests.csproj",
            "#",
            $"# {failures.Count} of {total} bash-comparable cases fail at this baseline.",
            string.Empty,
        ];

        lines.AddRange(failures
            .OrderBy(failure => failure.Case.Id, StringComparer.Ordinal)
            .Select(failure => $"{failure.Case.Id}  {Summarise(failure)}"));

        File.WriteAllLines(path, lines);
    }

    private static string Summarise(CaseOutcome failure)
    {
        string detail = failure.Detail.Split('\n')[0];
        return detail.Length <= 120 ? detail : detail[..120];
    }
}
