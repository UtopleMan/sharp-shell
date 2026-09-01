using System.Text.RegularExpressions;

namespace Sharp.Shell.Text;

// Recovers the POSIX extent of a match whose start .NET has already found.
//
// The two engines agree on where a match begins — both scan positions left to right and stop at the
// first that can match at all — and disagree on where it ends: POSIX takes the longest match at that
// start, .NET takes the first alternative that succeeds. So the start is taken from .NET and the end
// is re-derived here, by testing every candidate end from the longest possible downwards with a
// fully anchored pattern and keeping the first that matches.
//
// Anchors are why this needs four patterns rather than one. A candidate is tested as a window of the
// input, and .NET binds `\A` and `\z` to the window's edges — but awk's `^` and `$` mean the edges of
// the *whole* subject. An anchor that cannot hold at a given window's boundary is therefore rewritten
// to something unmatchable, which leaves one pattern per (window starts the input, window ends the
// input) pair.
internal sealed class LongestMatchFinder
{
    private const string StartAnchorEmission = @"\A";

    private const string EndAnchorEmission = @"\z";

    private const string Unmatchable = "(?!)";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    private readonly Regex wholeInput;

    private readonly Regex fromInputStart;

    private readonly Regex toInputEnd;

    private readonly Regex interior;

    private readonly int maximumLength;

    private LongestMatchFinder(RegexNode parsed)
    {
        wholeInput = Probe(parsed, keepsStartAnchor: true, keepsEndAnchor: true);
        fromInputStart = Probe(parsed, keepsStartAnchor: true, keepsEndAnchor: false);
        toInputEnd = Probe(parsed, keepsStartAnchor: false, keepsEndAnchor: true);
        interior = Probe(parsed, keepsStartAnchor: false, keepsEndAnchor: false);
        maximumLength = AlternationSafety.MaximumLength(parsed);
    }

    public static LongestMatchFinder For(RegexNode parsed) => new(parsed);

    // knownLength is what .NET already found at this start, which is a lower bound on the answer:
    // POSIX cannot be shorter, so the scan stops as soon as it reaches it.
    public int LongestLength(string input, int start, int knownLength)
    {
        int furthest = maximumLength == RegexRepeat.Unbounded
            ? input.Length
            : Math.Min(input.Length, start + maximumLength);

        for (int end = furthest; end > start + knownLength; end--)
        {
            if (Matches(input, start, end))
            {
                return end - start;
            }
        }

        return knownLength;
    }

    private bool Matches(string input, int start, int end) =>
        ProbeFor(start == 0, end == input.Length).Match(input, start, end - start).Success;

    private Regex ProbeFor(bool startsInput, bool endsInput) => (startsInput, endsInput) switch
    {
        (true, true) => wholeInput,
        (true, false) => fromInputStart,
        (false, true) => toInputEnd,
        (false, false) => interior,
    };

    private static Regex Probe(RegexNode parsed, bool keepsStartAnchor, bool keepsEndAnchor)
    {
        string body = RegexEmitter.Emit(WithAnchors(parsed, keepsStartAnchor, keepsEndAnchor));

        return new Regex($@"\A(?:{body})\z", RegexOptions.CultureInvariant, MatchTimeout);
    }

    private static RegexNode WithAnchors(RegexNode node, bool keepsStart, bool keepsEnd) => node switch
    {
        RegexAtom { IsZeroWidth: true, Emission: StartAnchorEmission } when !keepsStart => Nothing(),
        RegexAtom { IsZeroWidth: true, Emission: EndAnchorEmission } when !keepsEnd => Nothing(),
        RegexGroup group => new RegexGroup(WithAnchors(group.Body, keepsStart, keepsEnd)),
        RegexRepeat repeat => repeat with { Body = WithAnchors(repeat.Body, keepsStart, keepsEnd) },
        RegexSequence sequence => new RegexSequence([.. Rewritten(sequence.Items, keepsStart, keepsEnd)]),
        RegexAlternation alternation => new RegexAlternation([.. Rewritten(alternation.Branches, keepsStart, keepsEnd)]),
        _ => node,
    };

    private static IEnumerable<RegexNode> Rewritten(IReadOnlyList<RegexNode> nodes, bool keepsStart, bool keepsEnd) =>
        nodes.Select(node => WithAnchors(node, keepsStart, keepsEnd));

    private static RegexNode Nothing() => new RegexAtom(Unmatchable, IsZeroWidth: true);
}
