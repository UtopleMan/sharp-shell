namespace Sharp.Shell.Text;

// The parsed shape of a POSIX pattern. It exists because two questions cannot be answered while
// scanning: whether an alternation is safe to translate at all, and how to emit a construct whose
// meaning depends on where it sits.
internal abstract record RegexNode;

internal sealed record RegexLiteral(char Character) : RegexNode;

// An already-translated fragment: a character class, the any-character form, or a zero-width
// assertion. Emission is decided at parse time because it depends on multi-line mode.
internal sealed record RegexAtom(string Emission, bool IsZeroWidth) : RegexNode;

internal sealed record RegexBackreference(int Group) : RegexNode;

internal sealed record RegexGroup(RegexNode Body) : RegexNode;

internal sealed record RegexRepeat(RegexNode Body, int Minimum, int Maximum) : RegexNode
{
    public const int Unbounded = -1;
}

internal sealed record RegexSequence(IReadOnlyList<RegexNode> Items) : RegexNode;

internal sealed record RegexAlternation(IReadOnlyList<RegexNode> Branches) : RegexNode;
