namespace Sharp.Shell.Lexing;

// Never throws for bad input: an unterminated quote is a returned Error with no tokens, the same
// never-throws contract the host's manifest readers use.
public sealed record LexResult(IReadOnlyList<Token> Tokens, string? Error)
{
    public static LexResult Failed(string error) => new([], error);
}
