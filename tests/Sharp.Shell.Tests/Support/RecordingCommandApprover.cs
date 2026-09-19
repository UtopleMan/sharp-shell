namespace Sharp.Shell.Tests.Support;

internal sealed record ApprovalRequest(
    string Program,
    IReadOnlyList<string> Arguments,
    string CommandText,
    string WorkingDirectory,
    bool IsOwned);

// Records every command the executor asked about, and answers with whatever the test supplied.
internal sealed class RecordingCommandApprover(Func<ApprovalRequest, CommandApproval>? decide = null) : ICommandApprover
{
    public List<ApprovalRequest> Requests { get; } = [];

    // Whole-line requests are kept apart from per-command ones: a suite asserting the hook fired
    // three times for a loop would otherwise silently count a line nobody could decompose.
    public List<ApprovalRequest> Lines { get; } = [];

    public IReadOnlyList<string> Texts => [.. Requests.Select(request => request.CommandText)];

    public CommandApproval Approve(
        string program,
        IReadOnlyList<string> arguments,
        string commandText,
        string workingDirectory,
        bool isOwned,
        CancellationToken cancellationToken)
    {
        ApprovalRequest request = new(program, arguments, commandText, workingDirectory, isOwned);
        Requests.Add(request);

        return decide is null ? CommandApproval.Allowed : decide(request);
    }

    public CommandApproval ApproveLine(
        string commandLine,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ApprovalRequest request = new(commandLine, [], commandLine, workingDirectory, IsOwned: false);
        Lines.Add(request);

        return decide is null ? CommandApproval.Allowed : decide(request);
    }
}
