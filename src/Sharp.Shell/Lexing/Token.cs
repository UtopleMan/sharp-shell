namespace Sharp.Shell.Lexing;

public enum TokenKind
{
    Word,
    Operator,
    Newline,
    EndOfInput,
}

// Word tokens carry their parsed Word; operators carry their text, including any file-descriptor
// prefix that bound to them ("2>"). Position is the source offset the token started at.
//
// HereDocumentBody is set on a << or <<- operator once its body has been read, which happens at the
// next newline — after the token was already emitted. That is why the lexer patches the token in
// place rather than the parser reading the body itself.
public sealed record Token(TokenKind Kind, string Text, Word? Word, int Position, string? HereDocumentBody = null);
