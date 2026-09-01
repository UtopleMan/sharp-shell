namespace Sharp.Shell.Expansion;

// Two distinct failures, kept apart on purpose. UnsupportedReason is Rule 1: a form this shell does
// not implement, which escalates the whole line to a real shell. ErrorMessage is the shell reporting
// an ordinary runtime error (${x:?...}), which is a normal non-zero exit, not an escalation.
public sealed record ExpansionResult(IReadOnlyList<string> Fields, string? UnsupportedReason, string? ErrorMessage)
{
    public bool IsSupported => UnsupportedReason is null;

    public bool HasError => ErrorMessage is not null;

    public static ExpansionResult Ok(IReadOnlyList<string> fields) => new(fields, null, null);

    public static ExpansionResult Unsupported(string reason) => new([], reason, null);

    public static ExpansionResult Failed(string message) => new([], null, message);
}

// What a command substitution produced. The executor supplies this; the expander only asks.
public sealed record CommandSubstitution(int ExitCode, string Output);
