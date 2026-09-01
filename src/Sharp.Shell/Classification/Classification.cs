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
public sealed record Classification(
    ExecutionTier Tier,
    IReadOnlyList<string> UnownedPrograms,
    bool Mutates,
    string? Reason)
{
    public static Classification Owned(bool mutates) => new(ExecutionTier.Owned, [], mutates, null);

    public static Classification Native(IReadOnlyList<string> unownedPrograms, bool mutates, string reason) =>
        new(ExecutionTier.Native, unownedPrograms, mutates, reason);
}
