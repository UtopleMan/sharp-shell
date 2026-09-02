using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// Walks lazily, so `find . | head -5` stops after five entries instead of touring the whole tree.
// Like grep, it sees everything: no .gitignore filtering.
public sealed class FindApplet : IApplet
{
    private static readonly string[] SupportedPrimaries =
        ["-name", "-iname", "-type", "-maxdepth", "-mindepth", "-path", "-not", "-print", "!"];

    public string Name => "find";

    public bool Mutates => false;

    public OperandPositions FileOperandPositions(IReadOnlyList<string> arguments) =>
        OperandPositions.Reading(FindOptions.From(arguments).RootPositions);

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments)
    {
        foreach (string argument in arguments)
        {
            if (!FlagReader.IsFlag(argument) || SupportedPrimaries.Contains(argument, StringComparer.Ordinal))
            {
                continue;
            }

            return FlagSupport.Reject(argument);
        }

        return FlagSupport.Supported;
    }

    public AppletRun Run(AppletContext context)
    {
        FindOptions options = FindOptions.From(context.Arguments);
        AppletRun run = new();
        run.Output = Walk(options, context, run);
        return run;
    }

    private static IEnumerable<string> Walk(FindOptions options, AppletContext context, AppletRun run)
    {
        foreach (string start in options.Roots)
        {
            string absolute = context.State.Resolve(start);

            if (!context.State.IsInsideRoot(absolute))
            {
                context.WriteError($"find: {start}: outside the workspace\n");
                run.ExitCode = 1;
                continue;
            }

            if (!Directory.Exists(absolute) && !File.Exists(absolute))
            {
                context.WriteError($"find: {start}: No such file or directory\n");
                run.ExitCode = 1;
                continue;
            }

            foreach (string line in Descend(absolute, start, 0, options))
            {
                yield return line;
            }
        }
    }

    private static IEnumerable<string> Descend(string absolute, string display, int depth, FindOptions options)
    {
        if (depth >= options.MinimumDepth && options.Accepts(display, absolute))
        {
            yield return $"{display}\n";
        }

        if (depth >= options.MaximumDepth || !Directory.Exists(absolute))
        {
            yield break;
        }

        string[] entries = Directory.GetFileSystemEntries(absolute);
        Array.Sort(entries, StringComparer.Ordinal);

        foreach (string entry in entries)
        {
            foreach (string line in Descend(entry, $"{display}/{Path.GetFileName(entry)}", depth + 1, options))
            {
                yield return line;
            }
        }
    }

    private sealed record FindOptions(
        IReadOnlyList<string> Roots,
        IReadOnlyList<int> RootPositions,
        string? NamePattern,
        bool NameIgnoresCase,
        string? PathPattern,
        char? EntryType,
        int MaximumDepth,
        int MinimumDepth,
        bool Negated)
    {
        public bool Accepts(string display, string absolute)
        {
            bool matched = MatchesName(display) && MatchesPath(display) && MatchesType(absolute);
            return Negated ? !matched : matched;
        }

        private bool MatchesName(string display) =>
            NamePattern is null
            || PatternMatch(Path.GetFileName(display.TrimEnd('/')), NamePattern, NameIgnoresCase);

        private bool MatchesPath(string display) =>
            PathPattern is null || PatternMatch(display, PathPattern, ignoresCase: false);

        private bool MatchesType(string absolute) => EntryType switch
        {
            'f' => File.Exists(absolute),
            'd' => Directory.Exists(absolute),
            _ => true,
        };

        private static bool PatternMatch(string text, string pattern, bool ignoresCase) =>
            Expansion.PatternMatcher.Matches(
                ignoresCase ? text.ToLowerInvariant() : text,
                ignoresCase ? pattern.ToLowerInvariant() : pattern);

        public static FindOptions From(IReadOnlyList<string> arguments)
        {
            List<string> roots = [];
            List<int> rootPositions = [];
            string? namePattern = null;
            bool nameIgnoresCase = false;
            string? pathPattern = null;
            char? entryType = null;
            int maximumDepth = int.MaxValue;
            int minimumDepth = 0;
            bool negated = false;
            bool seenPrimary = false;

            for (int index = 0; index < arguments.Count; index++)
            {
                string argument = arguments[index];
                string? value = index + 1 < arguments.Count ? arguments[index + 1] : null;

                switch (argument)
                {
                    case "-name" when value is not null:
                        namePattern = value;
                        index++;
                        seenPrimary = true;
                        continue;
                    case "-iname" when value is not null:
                        namePattern = value;
                        nameIgnoresCase = true;
                        index++;
                        seenPrimary = true;
                        continue;
                    case "-path" when value is not null:
                        pathPattern = value;
                        index++;
                        seenPrimary = true;
                        continue;
                    case "-type" when value is not null:
                        entryType = value[0];
                        index++;
                        seenPrimary = true;
                        continue;
                    case "-maxdepth" when value is not null && int.TryParse(value, out int maximum):
                        maximumDepth = maximum;
                        index++;
                        seenPrimary = true;
                        continue;
                    case "-mindepth" when value is not null && int.TryParse(value, out int minimum):
                        minimumDepth = minimum;
                        index++;
                        seenPrimary = true;
                        continue;
                    case "-not" or "!":
                        negated = true;
                        seenPrimary = true;
                        continue;
                    case "-print":
                        seenPrimary = true;
                        continue;
                }

                if (!seenPrimary)
                {
                    roots.Add(argument);
                    rootPositions.Add(index);
                }
            }

            return new FindOptions(
                roots.Count > 0 ? roots : ["."],
                rootPositions,
                namePattern,
                nameIgnoresCase,
                pathPattern,
                entryType,
                maximumDepth,
                minimumDepth,
                negated);
        }
    }
}
