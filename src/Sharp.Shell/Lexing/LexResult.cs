namespace Sharp.Shell.Lexing;

// Never throws for bad input: an unterminated quote is a returned Error with no tokens, the same
// never-throws contract the host's manifest readers use.
//
// IsIncomplete says the input stopped in the middle of something rather than saying something
// wrong — another line could finish it. Every lexical failure is of that kind, because the only way
// to fail here is to leave a quote, an expansion or a here-document unclosed. A reader feeding the
// shell a script joins the next line on rather than running a fragment; that is what bash's PS2
// prompt is for.
public sealed record LexResult(IReadOnlyList<Token> Tokens, string? Error, bool IsIncomplete = false)
{
    public static LexResult Failed(string error) => new([], error, IsIncomplete: true);
}
