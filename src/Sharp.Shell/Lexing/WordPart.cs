namespace Sharp.Shell.Lexing;

public enum WordPartKind
{
    Literal,
    SingleQuoted,
    DoubleQuoted,
    Parameter,
    CommandSubstitution,
    Arithmetic,
    Tilde,
}

// One piece of a word, kept separate from its neighbours because quoting decides what may happen
// later: an unquoted expansion is field-split and globbed, a quoted one is not, and single-quoted
// text is never touched at all. Nested is the inside of a double-quoted run.
public sealed record WordPart(WordPartKind Kind, string Text, IReadOnlyList<WordPart>? Nested = null);

// A single shell word. IsFullyLiteral answers the question classification asks before anything
// runs: is this command name knowable without expanding it?
public sealed record Word(IReadOnlyList<WordPart> Parts)
{
    public bool IsFullyLiteral => Parts.All(IsLiteral);

    public string LiteralText => string.Concat(Parts.Select(LiteralOf));

    private static bool IsLiteral(WordPart part) => part.Kind switch
    {
        WordPartKind.Literal or WordPartKind.SingleQuoted => true,
        WordPartKind.DoubleQuoted => part.Nested is not null && part.Nested.All(IsLiteral),
        _ => false,
    };

    private static string LiteralOf(WordPart part) => part.Kind switch
    {
        WordPartKind.Literal or WordPartKind.SingleQuoted => part.Text,
        WordPartKind.DoubleQuoted when part.Nested is not null && part.Nested.All(IsLiteral) =>
            string.Concat(part.Nested.Select(LiteralOf)),
        _ => string.Empty,
    };
}
