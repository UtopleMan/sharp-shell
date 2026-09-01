namespace Sharp.Shell.Tests.Conformance;

// Which vendored files this project runs. The rule is data-driven rather than a curated list: a
// file is ours when its own `## compare_shells:` header names bash, which upstream keeps correct.
// The ysh-* files are excluded by that header without anyone maintaining an exclusion list.
public static class CorpusSelector
{
    public static string Directory =>
        Path.Combine(AppContext.BaseDirectory, "corpus", "oils", "spec");

    public static bool IsAvailable => System.IO.Directory.Exists(Directory);

    public static IReadOnlyList<SpecCase> Cases()
    {
        if (!IsAvailable)
        {
            return [];
        }

        List<SpecCase> cases = [];
        string[] files = System.IO.Directory.GetFiles(Directory, "*.test.sh");
        Array.Sort(files, StringComparer.Ordinal);

        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            if (!SpecFileParser.ComparesAgainstBash(text))
            {
                continue;
            }

            cases.AddRange(SpecFileParser.Parse(Path.GetFileName(file), text));
        }

        return cases;
    }
}
