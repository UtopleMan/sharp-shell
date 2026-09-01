namespace Sharp.Shell.Commands.Awk;

internal enum AwkTokenKind
{
    EndOfInput,
    Newline,
    Semicolon,

    Number,
    String,
    Regex,
    Name,

    // A name written immediately against its opening parenthesis, which is the only thing that tells
    // a user function call apart from a concatenation with a parenthesised expression.
    FunctionName,
    Builtin,

    Begin,
    End,
    Function,
    If,
    Else,
    While,
    For,
    Do,
    Break,
    Continue,
    Next,
    NextFile,
    Exit,
    Return,
    Delete,
    In,
    Print,
    Printf,
    Getline,

    LeftBrace,
    RightBrace,
    LeftParen,
    RightParen,
    LeftBracket,
    RightBracket,
    Comma,
    Question,
    Colon,

    Assign,
    AddAssign,
    SubtractAssign,
    MultiplyAssign,
    DivideAssign,
    ModuloAssign,
    PowerAssign,

    Or,
    And,
    Not,
    Match,
    NotMatch,

    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    Equal,
    NotEqual,

    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    Caret,

    Increment,
    Decrement,
    Dollar,
    Append,
    Pipe,
}

// Text carries the decoded value for a string, the pattern source for a regular expression, and the
// spelling for everything else; Number carries the value of a numeric literal.
internal sealed record AwkToken(AwkTokenKind Kind, string Text, double Number, int Line)
{
    public static AwkToken Of(AwkTokenKind kind, string text, int line) => new(kind, text, 0, line);
}
