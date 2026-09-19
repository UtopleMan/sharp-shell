namespace Sharp.Shell;

public enum ExecutionTier
{
    Owned,
    Native,
}

// The verdict on one command line, reached before any side effect. UnownedPrograms names every
// program that forced the escalation, because consent for an escape is per program: an "always"
// answer for git must not cover curl.
//
// Mutates is a separate axis from Tier on purpose. The sandbox bounds *where* a command can act,
// not *what* it does — `rm -rf .` inside the workspace is fully confined and still destroys the
// work — so the prompt policy needs both answers.
//
// Reads and Writes are the concrete files an owned line will open, resolved against the working
// directory after quoting and globbing, so the host can gate each one by path rather than by tool
// name. They are empty for a native line: that line runs as a real process with no preopen, and what
// it touches is unknowable from here.
//
// UnrunnableReason is the only verdict that stops the shell executing. It is set for a line this
// shell cannot run at all — one that will not parse, or that needs the process model the guest has
// no threads for — and such a line is handed to ICommandExecutor.ExecuteLine whole, as one decision.
// Not owning `git` is not one of those reasons any more: the shell runs the line and asks about
// `git` at the dispatch point like every other command.
//
// KnowsEveryRead and KnowsEveryWrite say whether each list is the whole truth. An operand that only
// becomes a path when the line runs — `cat "$file"`, `> $out` — is left out rather than guessed at,
// and the flag for its kind goes false so the caller falls back to a coarser question about that
// kind alone. They are separate because `touch "$out"` hides a write and reads nothing, and a
// coarse read question there would be a prompt about something that never happens.
public sealed record Classification(
    ExecutionTier Tier,
    IReadOnlyList<string> UnownedPrograms,
    bool Mutates,
    string? Reason,
    IReadOnlyList<string> Reads,
    IReadOnlyList<string> Writes,
    bool KnowsEveryRead,
    bool KnowsEveryWrite,
    string? UnrunnableReason = null)
{
    public bool IsRunnable => UnrunnableReason is null;

    public static Classification Owned(
        bool mutates,
        IReadOnlyList<string> reads,
        IReadOnlyList<string> writes,
        bool knowsEveryRead,
        bool knowsEveryWrite) =>
        new(ExecutionTier.Owned, [], mutates, null, reads, writes, knowsEveryRead, knowsEveryWrite);

    public static Classification Native(IReadOnlyList<string> unownedPrograms, bool mutates, string reason) =>
        new(ExecutionTier.Native, unownedPrograms, mutates, reason, [], [], false, false);

    public static Classification Unrunnable(IReadOnlyList<string> unownedPrograms, bool mutates, string reason) =>
        new(ExecutionTier.Native, unownedPrograms, mutates, reason, [], [], false, false, reason);
}
