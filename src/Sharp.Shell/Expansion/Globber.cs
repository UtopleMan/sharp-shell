using Sharp.Shell.Execution;

namespace Sharp.Shell.Expansion;

// Filesystem globbing over the real workspace. Unlike the host's glob *tool*, this applies no
// .gitignore filtering: POSIX globbing sees everything, and silently hiding files would be exactly
// the invisible-wrong-output failure Rule 1 exists to prevent.
//
// A pattern that matches nothing returns no matches; the caller applies bash's nullglob-off rule
// and keeps the pattern as literal text, because only the caller knows the unescaped original.
public static class Globber
{
    public static IReadOnlyList<string> Expand(string pattern, ShellState state)
    {
        if (!ContainsWildcard(pattern))
        {
            return [];
        }

        string[] segments = pattern.Split('/');
        bool rooted = pattern.StartsWith('/');
        string start = rooted ? Path.GetPathRoot(state.WorkingDirectory)! : state.WorkingDirectory;

        List<string> matches = [];
        Walk(start, segments, rooted ? 1 : 0, state, matches);

        matches.Sort(StringComparer.Ordinal);
        return [.. matches.Select(match => Relative(match, state, rooted))];
    }

    public static bool ContainsWildcard(string text) => text.AsSpan().IndexOfAny('*', '?', '[') >= 0;

    private static void Walk(string directory, string[] segments, int segmentIndex, ShellState state, List<string> matches)
    {
        if (!state.IsInsideRoot(directory) || !Directory.Exists(directory))
        {
            return;
        }

        if (segmentIndex >= segments.Length)
        {
            matches.Add(directory);
            return;
        }

        string segment = segments[segmentIndex];
        bool isLast = segmentIndex == segments.Length - 1;

        if (segment == "**")
        {
            Walk(directory, segments, segmentIndex + 1, state, matches);
            foreach (string child in SortedDirectories(directory))
            {
                Walk(child, segments, segmentIndex, state, matches);
            }

            return;
        }

        if (segment.Length == 0)
        {
            Walk(directory, segments, segmentIndex + 1, state, matches);
            return;
        }

        foreach (string entry in SortedEntries(directory))
        {
            string name = Path.GetFileName(entry);
            if (!IsCandidate(name, segment) || !PatternMatcher.Matches(name, segment))
            {
                continue;
            }

            if (isLast)
            {
                matches.Add(entry);
                continue;
            }

            Walk(entry, segments, segmentIndex + 1, state, matches);
        }
    }

    // POSIX: a leading dot is matched only by an explicit dot in the pattern.
    private static bool IsCandidate(string name, string segment) => !name.StartsWith('.') || segment.StartsWith('.');

    private static IEnumerable<string> SortedEntries(string directory)
    {
        string[] entries = Directory.GetFileSystemEntries(directory);
        Array.Sort(entries, StringComparer.Ordinal);
        return entries;
    }

    private static IEnumerable<string> SortedDirectories(string directory)
    {
        string[] directories = Directory.GetDirectories(directory);
        Array.Sort(directories, StringComparer.Ordinal);
        return directories;
    }

    private static string Relative(string absolute, ShellState state, bool rooted) =>
        rooted ? absolute : Path.GetRelativePath(state.WorkingDirectory, absolute);
}
