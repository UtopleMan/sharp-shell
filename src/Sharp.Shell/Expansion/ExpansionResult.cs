namespace Sharp.Shell.Expansion;

// Two distinct failures, kept apart on purpose. UnsupportedReason is Rule 1: a form this shell does
// not implement, which escalates the whole line to a real shell. ErrorMessage is the shell reporting
// an ordinary runtime error (${x:?...}), which is a normal non-zero exit, not an escalation.
public sealed record ExpansionResult(
    IReadOnlyList<string> Fields,
    string? UnsupportedReason,
    string? ErrorMessage,
    bool IsFatal = false)
{
    public bool IsSupported => UnsupportedReason is null;

    public bool HasError => ErrorMessage is not null;

    public static ExpansionResult Ok(IReadOnlyList<string> fields) => new(fields, null, null);

    public static ExpansionResult Unsupported(string reason) => new([], reason, null);

    // Fatal means the run stops rather than the command merely failing: an unbound variable under
    // nounset is the shell refusing to carry on, not a command reporting a bad day.
    public static ExpansionResult Failed(string message, bool isFatal = false) => new([], null, message, isFatal);
}

// What a command substitution produced. The executor supplies this; the expander only asks.
public sealed record CommandSubstitution(int ExitCode, string Output);
