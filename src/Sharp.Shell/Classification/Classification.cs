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
// Reads and Writes are the concrete files an owned line will open, resolved against the workspace
// after quoting and globbing, so the host can gate each one by path rather than by tool name. They
// are empty for a native line: that line runs as a real process with no preopen, and what it
// touches is unknowable from here.
//
// KnowsEveryFile says whether that list is the whole truth. An operand that only becomes a path when
// the line runs — `cat "$file"`, `> $out` — is left out rather than guessed at, and this goes false
// so the caller can fall back to a coarser answer instead of trusting a short list.
public sealed record Classification(
    ExecutionTier Tier,
    IReadOnlyList<string> UnownedPrograms,
    bool Mutates,
    string? Reason,
    IReadOnlyList<string> Reads,
    IReadOnlyList<string> Writes,
    bool KnowsEveryFile)
{
    public static Classification Owned(
        bool mutates,
        IReadOnlyList<string> reads,
        IReadOnlyList<string> writes,
        bool knowsEveryFile) =>
        new(ExecutionTier.Owned, [], mutates, null, reads, writes, knowsEveryFile);

    public static Classification Native(IReadOnlyList<string> unownedPrograms, bool mutates, string reason) =>
        new(ExecutionTier.Native, unownedPrograms, mutates, reason, [], [], false);
}
