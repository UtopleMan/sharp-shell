using System.Text;
using Sharp.Shell.Parsing;

namespace Sharp;

// Joins the lines of one command. A command is not always one line — a function body, a multi-line
// `if`, a here-document all span several — and running them a line at a time runs fragments: the
// body of an `if` executes without its condition ever being evaluated, and a here-document's text
// is taken for commands. This is what bash's PS2 prompt is for.
//
// The parser decides when to stop: it answers whether the input so far is merely unfinished, which
// is a different question from whether it is wrong. `fi` on its own is wrong and runs immediately
// so its error is reported where it happened; `if true; then` is unfinished and waits.
internal sealed class CommandReader
{
    private readonly StringBuilder pending = new();

    public bool IsContinuing => pending.Length > 0;

    // The lines read so far, for the caller to run when the input ends mid-command. An unfinished
    // command still has to be handed over rather than dropped, so that it reports its own error.
    public string Pending => pending.ToString();

    // The reason this command is malformed, or null when it is not. A construct this shell does not
    // implement is not malformed: it is valid bash, handed to a real shell, and a script runs on
    // past it. Input bash itself would refuse stops a non-interactive shell where it stands.
    public static string? SyntaxErrorIn(string command) =>
        Parser.Parse(command) is { IsSyntaxError: true } failed ? failed.UnsupportedReason : null;

    // Ctrl+C at a continuation prompt throws the half-typed command away, as it does in bash.
    // Without it an unterminated quote traps the shell: every line after it, `exit` included, is
    // swallowed into a command that can never complete.
    public void Abandon() => pending.Clear();

    public bool TryComplete(string line, out string command)
    {
        pending.Append(line).Append('\n');
        command = pending.ToString();

        if (Parser.Parse(command).IsIncomplete)
        {
            command = string.Empty;
            return false;
        }

        pending.Clear();
        return true;
    }
}
