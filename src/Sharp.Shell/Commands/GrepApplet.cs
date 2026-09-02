using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Sharp.Shell.Execution;
using Sharp.Shell.Text;

namespace Sharp.Shell.Commands;

// Streams its matches so `grep pattern big | head -1` reads one match, not the whole file.
//
// The pattern is a POSIX basic regular expression, or an extended one under -E, translated to .NET
// by PosixRegexTranslator; a pattern the translator will not vouch for is refused, so the command
// escalates rather than answering in a different dialect.
//
// One deliberate difference from GNU grep remains, recorded rather than hidden: this applet sees
// every file, with no .gitignore filtering — the host's grep *tool* filters, the shell applet
// follows POSIX.
public sealed class GrepApplet : IApplet
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    public string Name => "grep";

    public bool Mutates => false;

    public IReadOnlyList<string> BundleableFlags => ShortFlags;

    public OperandPositions FileOperandPositions(IReadOnlyList<string> arguments) =>
        OperandPositions.Reading(GrepOptions.From(arguments).FilePositions);

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments)
    {
        foreach (string argument in arguments)
        {
            if (argument == "--")
            {
                break;
            }

            if (!FlagReader.IsFlag(argument) || IsSupportedFlag(argument))
            {
                continue;
            }

            return FlagSupport.Reject(argument);
        }

        GrepOptions options = GrepOptions.From(arguments);

        // GNU prints nothing for -o -v and BSD prints the whole non-matching line. Rather than pick
        // a dialect for a combination nobody means, hand the line back.
        if (options.MatchesOnly && options.Inverts)
        {
            return FlagSupport.Reject("-o with -v");
        }

        if (options.Pattern is null || options.FixedStrings)
        {
            return FlagSupport.Supported;
        }

        RegexTranslation translation = Translate(options);

        return translation.IsTranslated
            ? FlagSupport.Supported
            : FlagSupport.Reject($"the pattern `{options.Pattern}': {translation.RefusalReason}");
    }

    // The single-letter flags are also the bundleable ones: `-ri` means the same as `-r -i`.
    private static readonly string[] ShortFlags =
        ["-i", "-v", "-n", "-r", "-R", "-l", "-c", "-E", "-F", "-e", "-h", "-w", "-o"];

    private static readonly FrozenSet<string> SupportedFlags =
        FrozenSet.ToFrozenSet([.. ShortFlags, "--include", "--exclude"], StringComparer.Ordinal);

    private static bool IsSupportedFlag(string argument) =>
        SupportedFlags.Contains(argument)
        || argument.StartsWith("--include=", StringComparison.Ordinal)
        || argument.StartsWith("--exclude=", StringComparison.Ordinal);

    public AppletRun Run(AppletContext context)
    {
        GrepOptions options = GrepOptions.From(context.Arguments);

        if (options.Pattern is null)
        {
            context.WriteError("grep: usage: grep [options] pattern [file...]\n");
            return AppletRun.Failed(2);
        }

        if (!options.FixedStrings && !Translate(options).IsTranslated)
        {
            context.WriteError($"grep: unsupported pattern: {options.Pattern}\n");
            return AppletRun.Failed(2);
        }

        AppletRun run = new() { ExitCode = 1 };
        run.Output = Search(options, context, run);
        return run;
    }

    private static IEnumerable<string> Search(GrepOptions options, AppletContext context, AppletRun run)
    {
        Func<string, bool> matches = MatcherFor(options);
        Regex? extents = options.MatchesOnly ? RegexFor(options) : null;
        IReadOnlyList<GrepSource> sources = SourcesFor(options, context, run);
        bool showsName = (sources.Count > 1 || options.Recurses) && !options.HidesNames;

        foreach (GrepSource source in sources)
        {
            int count = 0;
            int lineNumber = 0;

            foreach (string line in TextStream.Lines(source.Read()))
            {
                lineNumber++;
                if (matches(line) == options.Inverts)
                {
                    continue;
                }

                run.ExitCode = 0;
                count++;

                if (options.CountsOnly)
                {
                    continue;
                }

                if (options.NamesOnly)
                {
                    break;
                }

                foreach (string reported in Reported(line, extents))
                {
                    yield return Format(reported, source.Name, lineNumber, showsName, options.NumbersLines);
                }
            }

            foreach (string summary in Summaries(options, source, count, showsName))
            {
                yield return summary;
            }
        }
    }

    private static IEnumerable<string> Summaries(GrepOptions options, GrepSource source, int count, bool showsName)
    {
        if (options.NamesOnly && count > 0)
        {
            yield return $"{source.Name}\n";
            yield break;
        }

        if (!options.CountsOnly)
        {
            yield break;
        }

        yield return showsName ? $"{source.Name}:{count}\n" : $"{count}\n";
    }

    private static string Format(string line, string name, int lineNumber, bool showsName, bool numbersLines)
    {
        string prefix = showsName ? $"{name}:" : string.Empty;
        string numbered = numbersLines ? $"{lineNumber}:" : string.Empty;

        return $"{prefix}{numbered}{line}\n";
    }

    // Without -o the whole line is the answer. With it, each non-empty match is — an empty match
    // still makes the line count, which is why `grep -o 'x*'` on `abc` prints nothing and exits 0.
    private static IEnumerable<string> Reported(string line, Regex? extents)
    {
        if (extents is null)
        {
            yield return line;
            yield break;
        }

        foreach (Match match in extents.Matches(line))
        {
            if (match.Length > 0)
            {
                yield return match.Value;
            }
        }
    }

    private static Func<string, bool> MatcherFor(GrepOptions options)
    {
        if (options.FixedStrings && !options.MatchesWholeWords)
        {
            StringComparison comparison = options.IgnoresCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return line => line.Contains(options.Pattern!, comparison);
        }

        return RegexFor(options).IsMatch;
    }

    private static Regex RegexFor(GrepOptions options)
    {
        RegexOptions regexOptions = RegexOptions.CultureInvariant;

        if (options.IgnoresCase)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        string pattern = options.FixedStrings
            ? Regex.Escape(options.Pattern!)
            : Translate(options).Pattern!;

        return new Regex(WordBounded(pattern, options.MatchesWholeWords), regexOptions, MatchTimeout);
    }

    // grep reports whole lines, never the matched span, so an alternation whose branches match
    // different lengths cannot change its answer — until -o, where the span *is* the answer.
    private static RegexTranslation Translate(GrepOptions options) => PosixRegexTranslator.Translate(
        options.Pattern!,
        options.ExtendedRegex ? RegexDialect.ExtendedPosix : RegexDialect.BasicPosix,
        usesMatchExtent: options.MatchesOnly);

    // -w means the match must be a whole word, which grep defines with the same ASCII word
    // characters the translator uses for \w and \b.
    private static string WordBounded(string pattern, bool matchesWholeWords) =>
        matchesWholeWords
            ? $"(?<![A-Za-z0-9_])(?:{pattern})(?![A-Za-z0-9_])"
            : pattern;

    private static IReadOnlyList<GrepSource> SourcesFor(GrepOptions options, AppletContext context, AppletRun run)
    {
        if (options.Files.Count == 0)
        {
            return [new GrepSource("(standard input)", () => context.Input)];
        }

        List<GrepSource> sources = [];
        foreach (string file in options.Files)
        {
            string absolute = context.State.Resolve(file);

            if (!context.State.IsInsideRoot(absolute))
            {
                context.WriteError($"grep: {file}: outside the workspace\n");
                run.ExitCode = 2;
                continue;
            }

            if (options.Recurses && Directory.Exists(absolute))
            {
                sources.AddRange(Descend(absolute, file).Where(source => options.Accepts(source.Name)));
                continue;
            }

            if (!File.Exists(absolute))
            {
                context.WriteError($"grep: {file}: No such file or directory\n");
                run.ExitCode = 2;
                continue;
            }

            sources.Add(new GrepSource(file, () => FileChunks.Read(absolute)));
        }

        return sources;
    }

    private static IEnumerable<GrepSource> Descend(string absolute, string display)
    {
        string[] entries = Directory.GetFileSystemEntries(absolute);
        Array.Sort(entries, StringComparer.Ordinal);

        foreach (string entry in entries)
        {
            string name = Path.GetFileName(entry);
            string shown = $"{display}/{name}";

            if (Directory.Exists(entry))
            {
                foreach (GrepSource nested in Descend(entry, shown))
                {
                    yield return nested;
                }

                continue;
            }

            yield return new GrepSource(shown, () => FileChunks.Read(entry));
        }
    }

    private sealed record GrepSource(string Name, Func<IEnumerable<string>> Read);

    private sealed record GrepOptions(
        string? Pattern,
        IReadOnlyList<string> Files,
        IReadOnlyList<int> FilePositions,
        string? IncludeGlob,
        string? ExcludeGlob,
        bool IgnoresCase,
        bool Inverts,
        bool NumbersLines,
        bool Recurses,
        bool NamesOnly,
        bool CountsOnly,
        bool FixedStrings,
        bool ExtendedRegex,
        bool MatchesWholeWords,
        bool HidesNames,
        bool MatchesOnly)
    {
        // --include/--exclude filter which files a recursive search reads, matched on the file name
        // the way GNU grep matches them.
        public bool Accepts(string path)
        {
            string name = Path.GetFileName(path);

            if (IncludeGlob is not null && !Expansion.PatternMatcher.Matches(name, IncludeGlob))
            {
                return false;
            }

            return ExcludeGlob is null || !Expansion.PatternMatcher.Matches(name, ExcludeGlob);
        }

        public static GrepOptions From(IReadOnlyList<string> arguments)
        {
            string? pattern = null;
            List<string> files = [];
            List<int> filePositions = [];
            string? includeGlob = null;
            string? excludeGlob = null;
            bool ignoresCase = false;
            bool inverts = false;
            bool numbersLines = false;
            bool recurses = false;
            bool namesOnly = false;
            bool countsOnly = false;
            bool fixedStrings = false;
            bool extendedRegex = false;
            bool matchesWholeWords = false;
            bool hidesNames = false;
            bool matchesOnly = false;

            for (int index = 0; index < arguments.Count; index++)
            {
                string argument = arguments[index];

                if (argument == "-e" && index + 1 < arguments.Count)
                {
                    pattern = arguments[++index];
                    continue;
                }

                if (TryReadGlob(arguments, ref index, "--include", ref includeGlob)
                    || TryReadGlob(arguments, ref index, "--exclude", ref excludeGlob))
                {
                    continue;
                }

                if (!FlagReader.IsFlag(argument))
                {
                    if (pattern is null)
                    {
                        pattern = argument;
                        continue;
                    }

                    files.Add(argument);
                    filePositions.Add(index);
                    continue;
                }

                ignoresCase |= argument == "-i";
                inverts |= argument == "-v";
                numbersLines |= argument == "-n";
                recurses |= argument is "-r" or "-R";
                namesOnly |= argument == "-l";
                countsOnly |= argument == "-c";
                fixedStrings |= argument == "-F";
                extendedRegex |= argument == "-E";
                matchesWholeWords |= argument == "-w";
                hidesNames |= argument == "-h";
                matchesOnly |= argument == "-o";
            }

            return new GrepOptions(
                pattern, files, filePositions, includeGlob, excludeGlob, ignoresCase, inverts, numbersLines,
                recurses, namesOnly, countsOnly, fixedStrings, extendedRegex, matchesWholeWords,
                hidesNames, matchesOnly);
        }

        // --include=GLOB and --include GLOB are both spelled in the wild.
        private static bool TryReadGlob(IReadOnlyList<string> arguments, ref int index, string flag, ref string? glob)
        {
            string argument = arguments[index];

            if (argument.StartsWith($"{flag}=", StringComparison.Ordinal))
            {
                glob = argument[(flag.Length + 1)..];
                return true;
            }

            if (argument == flag && index + 1 < arguments.Count)
            {
                glob = arguments[++index];
                return true;
            }

            return false;
        }
    }
}
