using Sharp.Shell;

namespace Sharp;

// --strict's policy, and the sandboxed guest's: a name the shell does not own is refused rather than
// escaped to. The line still runs — the owned commands ahead of the refusal do their work — and the
// refusal names the program that stopped it.
//
// A line the shell cannot run at all is allowed through to the executor, which under --strict is
// NotSupportedCommandExecutor and can start nothing. The reason reported is then the line's own,
// which says more than a blanket refusal would.
internal sealed class OwnedOnlyCommandApprover : ICommandApprover
{
    public CommandApproval Approve(
        string program,
        IReadOnlyList<string> arguments,
        string commandText,
        string workingDirectory,
        bool isOwned,
        CancellationToken cancellationToken) =>
        isOwned
            ? CommandApproval.Allowed
            : CommandApproval.Deny($"'{program}' is not one of the sandboxed commands");

    public CommandApproval ApproveLine(
        string commandLine,
        string workingDirectory,
        CancellationToken cancellationToken) => CommandApproval.Allowed;
}
