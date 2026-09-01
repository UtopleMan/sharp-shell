using System.Text.RegularExpressions;

namespace Sharp.Shell.Text;

// The one place awk touches a regular expression: a translated POSIX ERE plus, where the pattern
// needs it, the layer that recovers the leftmost-longest extent.
//
// awk needs no rule about submatches, unlike sed. POSIX EREs have no backreferences and awk exposes
// no capture groups — `match`, `sub`, `gsub`, `split` and field splitting all observe the whole-match
// extent and nothing inside it — so recovering that extent is the whole of the difference between
// .NET's answer and POSIX's.
internal sealed class AwkRegex
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    private readonly Regex regex;

    private readonly LongestMatchFinder? extent;

    private AwkRegex(Regex regex, LongestMatchFinder? extent)
    {
        this.regex = regex;
        this.extent = extent;
    }

    public static bool TryCreate(string pattern, out AwkRegex? compiled, out string? refusal)
    {
        PosixRegexProgram program = PosixRegexTranslator.TranslateForExtent(pattern);

        if (!program.IsTranslated)
        {
            compiled = null;
            refusal = program.RefusalReason;
            return false;
        }

        compiled = new AwkRegex(
            new Regex(program.Pattern!, RegexOptions.CultureInvariant, MatchTimeout),
            program.Extent);

        refusal = null;
        return true;
    }

    public bool IsMatch(string input) => regex.IsMatch(input);

    // startAt is a scan position, not a new subject: `^` still means the start of the whole string,
    // which is what stops `gsub(/^a/, "X")` from replacing every leading run in turn.
    public (int Start, int Length)? Find(string input, int startAt)
    {
        if (startAt > input.Length)
        {
            return null;
        }

        Match match = regex.Match(input, startAt);

        if (!match.Success)
        {
            return null;
        }

        return (match.Index, extent?.LongestLength(input, match.Index, match.Length) ?? match.Length);
    }
}
