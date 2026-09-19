using Sharp.Shell;

namespace Sharp;

// --explain's per-command half. The decisions are collected rather than printed as they happen: the
// session prints the line's classification first, and a decision that appeared before it would read
// as belonging to the previous line.
internal sealed class ExplainingCommandApprover(ICommandApprover inner) : ICommandApprover
{
    private readonly List<string> decisions = [];

    public IReadOnlyList<string> Decisions => decisions;

    public void Forget() => decisions.Clear();

    public CommandApproval Approve(
        string program,
        IReadOnlyList<string> arguments,
        string commandText,
        string workingDirectory,
        bool isOwned,
        CancellationToken cancellationToken)
    {
        CommandApproval approval = inner.Approve(
            program, arguments, commandText, workingDirectory, isOwned, cancellationToken);

        decisions.Add($"  {(isOwned ? "owned" : "native")} {commandText}{Outcome(approval)}");

        return approval;
    }

    public CommandApproval ApproveLine(
        string commandLine,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        CommandApproval approval = inner.ApproveLine(commandLine, workingDirectory, cancellationToken);

        decisions.Add($"  whole line {commandLine}{Outcome(approval)}");

        return approval;
    }

    private static string Outcome(CommandApproval approval) =>
        approval.IsAllowed ? string.Empty : $" — refused: {approval.Reason}";
}
