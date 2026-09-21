namespace Sharp.Shell;

// One command's verdict. Reason is the text the shell prints and the host's chance to say why in
// its own words; a denial without one still stops the command.
public sealed record CommandApproval(bool IsAllowed, string? Reason)
{
    public static CommandApproval Allowed { get; } = new(true, null);

    public static CommandApproval Deny(string reason) => new(false, reason);
}

// The second host seam, beside ICommandExecutor: consulted once per command, at the dispatch point,
// with the program and its arguments already expanded. A host never has to parse bash to know what
// it is consenting to — `rm $f` on the third iteration of a loop arrives here as `rm build/c.cs`.
//
// commandText is `CommandText.Of(program, arguments)`: one canonical spelling, so every host names
// the same target the same way instead of each joining the arguments its own way. It is
// post-expansion — `dotnet $VERB` arrives as `dotnet build` — which is what makes it worth matching
// rules against, and is also not what the user typed.
//
// isOwned says whether the shell is about to run its own applet or reach for a real program. It is
// information, not policy: a host may consent to a sandboxed applet and refuse the same name as a
// process, and only the shell knows which it is about to do.
//
// isNonDestructive is the narrower claim, and the only one a standing "all read-only commands"
// consent may be keyed on: an applet the shell implements itself, whose own code declares this
// invocation non-mutating, with nothing redirected out of the default streams. `sed -n 1,5p f` is
// in and `sed -i s/a/b/ f` is out; a shell function is out however it is named; a native program is
// out, because the shell cannot see inside one. False means ask — it is never a claim that the
// command writes, only that the shell will not vouch for it.
public interface ICommandApprover
{
    CommandApproval Approve(
        string program,
        IReadOnlyList<string> arguments,
        string commandText,
        string workingDirectory,
        bool isOwned,
        bool isNonDestructive,
        CancellationToken cancellationToken);

    // The other request the shell makes, and the coarse one: a line it cannot run itself, handed to
    // ICommandExecutor.ExecuteLine whole. There is no per-command question to ask here — that is
    // exactly what "the shell cannot run this" means — so the line is the target.
    CommandApproval ApproveLine(
        string commandLine,
        string workingDirectory,
        CancellationToken cancellationToken);
}

// The default, so a host that plugs nothing in sees the behaviour it saw before the seam existed.
public sealed class AllowAllCommandApprover : ICommandApprover
{
    public CommandApproval Approve(
        string program,
        IReadOnlyList<string> arguments,
        string commandText,
        string workingDirectory,
        bool isOwned,
        bool isNonDestructive,
        CancellationToken cancellationToken) => CommandApproval.Allowed;

    public CommandApproval ApproveLine(
        string commandLine,
        string workingDirectory,
        CancellationToken cancellationToken) => CommandApproval.Allowed;
}

// The fail-closed posture, mirroring NotSupportedCommandExecutor: nothing runs until a real
// approver says so.
public sealed class DenyingCommandApprover : ICommandApprover
{
    public CommandApproval Approve(
        string program,
        IReadOnlyList<string> arguments,
        string commandText,
        string workingDirectory,
        bool isOwned,
        bool isNonDestructive,
        CancellationToken cancellationToken) => CommandApproval.Deny($"{program}: not approved");

    public CommandApproval ApproveLine(
        string commandLine,
        string workingDirectory,
        CancellationToken cancellationToken) => CommandApproval.Deny("the command line was not approved");
}
