namespace Sharp.Shell.Text;

// POSIX matches leftmost-longest; .NET matches leftmost-first. The two only disagree when one
// alternative can match a prefix of what another matches at the same position, and no rewrite fixes
// that — so a pattern where it could happen is refused instead of answered differently.
//
// Two shapes are provably safe: every branch the same fixed length, or every branch a literal with
// no branch a prefix of another.
internal static class AlternationSafety
{
    public const string Reason =
        "alternation whose branches can match different lengths (POSIX matches leftmost-longest, .NET leftmost-first)";

    // The same question as Check, for a caller that recovers the extent instead of refusing: false
    // means the pattern needs the leftmost-longest scan, not that it cannot be translated.
    public static bool IsExtentSafe(RegexNode node) => Check(node) is null;

    public static int MaximumLength(RegexNode node) => MeasureLength(node).Maximum;

    public static string? Check(RegexNode node)
    {
        if (node is RegexAlternation alternation && !IsSafe(alternation))
        {
            return Reason;
        }

        foreach (RegexNode child in Children(node))
        {
            string? nested = Check(child);

            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool IsSafe(RegexAlternation alternation)
    {
        List<(int Minimum, int Maximum)> lengths = [.. alternation.Branches.Select(MeasureLength)];

        if (lengths.All(length => length.Minimum == length.Maximum && length.Minimum == lengths[0].Minimum))
        {
            return true;
        }

        List<string?> literals = [.. alternation.Branches.Select(LiteralTextOf)];

        return literals.All(text => text is not null) && !HasPrefixRelation(literals!);
    }

    private static bool HasPrefixRelation(List<string> literals)
    {
        foreach (string outer in literals)
        {
            foreach (string inner in literals)
            {
                if (!ReferenceEquals(outer, inner)
                    && outer.Length != inner.Length
                    && outer.StartsWith(inner, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<RegexNode> Children(RegexNode node) => node switch
    {
        RegexGroup group => [group.Body],
        RegexRepeat repeat => [repeat.Body],
        RegexSequence sequence => sequence.Items,
        RegexAlternation alternation => alternation.Branches,
        _ => [],
    };

    private static (int Minimum, int Maximum) MeasureLength(RegexNode node) => node switch
    {
        RegexLiteral => (1, 1),
        RegexAtom atom => atom.IsZeroWidth ? (0, 0) : (1, 1),
        RegexBackreference => (0, RegexRepeat.Unbounded),
        RegexGroup group => MeasureLength(group.Body),
        RegexRepeat repeat => MeasureRepeat(repeat),
        RegexSequence sequence => MeasureSequence(sequence),
        RegexAlternation alternation => MeasureAlternation(alternation),
        _ => (0, 0),
    };

    private static (int Minimum, int Maximum) MeasureRepeat(RegexRepeat repeat)
    {
        (int bodyMinimum, int bodyMaximum) = MeasureLength(repeat.Body);
        int maximum = repeat.Maximum == RegexRepeat.Unbounded || bodyMaximum == RegexRepeat.Unbounded
            ? RegexRepeat.Unbounded
            : repeat.Maximum * bodyMaximum;

        return (repeat.Minimum * bodyMinimum, maximum);
    }

    private static (int Minimum, int Maximum) MeasureSequence(RegexSequence sequence)
    {
        int minimum = 0;
        int maximum = 0;

        foreach (RegexNode item in sequence.Items)
        {
            (int itemMinimum, int itemMaximum) = MeasureLength(item);
            minimum += itemMinimum;
            maximum = maximum == RegexRepeat.Unbounded || itemMaximum == RegexRepeat.Unbounded
                ? RegexRepeat.Unbounded
                : maximum + itemMaximum;
        }

        return (minimum, maximum);
    }

    private static (int Minimum, int Maximum) MeasureAlternation(RegexAlternation alternation)
    {
        List<(int Minimum, int Maximum)> lengths = [.. alternation.Branches.Select(MeasureLength)];
        int maximum = lengths.Any(length => length.Maximum == RegexRepeat.Unbounded)
            ? RegexRepeat.Unbounded
            : lengths.Max(length => length.Maximum);

        return (lengths.Min(length => length.Minimum), maximum);
    }

    private static string? LiteralTextOf(RegexNode node) => node switch
    {
        RegexLiteral literal => literal.Character.ToString(),
        RegexGroup group => LiteralTextOf(group.Body),
        RegexSequence sequence => LiteralTextOfSequence(sequence),
        _ => null,
    };

    private static string? LiteralTextOfSequence(RegexSequence sequence)
    {
        List<string?> parts = [.. sequence.Items.Select(LiteralTextOf)];

        return parts.Any(part => part is null) ? null : string.Concat(parts);
    }
}
