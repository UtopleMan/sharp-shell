namespace Sharp.Shell.Parsing;

// A parse either produced a program or named a reason it could not. UnsupportedReason covers both
// malformed input and constructs this shell deliberately does not implement, because the caller
// treats them identically: the whole line goes to the native tier untouched.
public sealed record ParseResult(ShellNode? Program, string? UnsupportedReason)
{
    public bool IsParsed => Program is not null;

    public static ParseResult Parsed(ShellNode program) => new(program, null);

    public static ParseResult Unsupported(string reason) => new(null, reason);
}
