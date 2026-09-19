namespace Sharp.Shell.Parsing;

// A parse either produced a program or named a reason it could not.
//
// Three different answers hide behind that reason, and a script runner has to tell them apart:
//
//   IsIncomplete           one line short. `if true; then` wants another line, and this can be true
//                          of a parse that *succeeded* — `cat << EOF` is a valid command still
//                          waiting for its body.
//   IsUnsupportedConstruct valid bash this shell does not implement — `&`, `trap`, process
//                          substitution. Not an error: the line is handed to a real shell.
//   IsSyntaxError          neither of those. Malformed input that bash would refuse too, which is
//                          why a non-interactive shell stops at it rather than running on.
public sealed record ParseResult(
    ShellNode? Program,
    string? UnsupportedReason,
    bool IsIncomplete = false,
    bool IsUnsupportedConstruct = false)
{
    public bool IsParsed => Program is not null;

    public bool IsSyntaxError => !IsParsed && !IsUnsupportedConstruct;

    public static ParseResult Parsed(ShellNode program) => new(program, null);

    public static ParseResult Unsupported(string reason) => new(null, reason);
}
