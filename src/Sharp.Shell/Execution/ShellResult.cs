namespace Sharp.Shell.Execution;

// RefusalReason is non-null exactly when the run stopped because the host's approver denied a
// command. It is the reliable channel: the same text goes to stderr, but stderr is where every
// other failure writes too, and a host should not have to parse it to tell consent from error.
public sealed record ShellResult(int ExitCode, string Stdout, string Stderr, string? RefusalReason = null)
{
    public bool WasRefused => RefusalReason is not null;
}
