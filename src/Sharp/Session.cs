using Sharp.Shell;
using Sharp.Shell.Commands;
using Sharp.Shell.Execution;

namespace Sharp;

// One shell session: the owned command set, the classifier behind it, and the state that persists
// between lines.
//
// State persisting is the one deliberate difference from the sandboxed tool, where every bash call
// gets a fresh ShellState. A tool call is a one-shot; a shell is a conversation, so `cd` sticks.
internal sealed class Session
{
    private readonly ShellExecutor executor;
    private readonly ExplainingCommandApprover? explaining;
    private readonly ShellState state;
    private readonly SessionSettings settings;
    private readonly TextWriter output;
    private readonly TextWriter error;

    // ShellState starts at its root, which for an unconfined session is "/" — not where the user
    // ran the shell. A shell that opens somewhere other than your current directory is useless, so
    // the session moves there first.
    //
    // The two writers are arguments rather than Console: the binary passes Console.Out and
    // Console.Error, and a test passes a StringWriter and reads what the session said.
    public Session(SessionSettings settings, TextWriter output, TextWriter error)
    {
        this.settings = settings;
        this.output = output;
        this.error = error;
        state = new ShellState(settings.Root);

        explaining = settings.Explains ? new ExplainingCommandApprover(Policy(settings)) : null;
        executor = new ShellExecutor(
            AppletRegistry.CreateDefault(),
            settings.Strict ? new NotSupportedCommandExecutor() : new NativeTier(),
            explaining ?? Policy(settings));

        if (!state.TryChangeDirectory(settings.StartDirectory, out string failure))
        {
            error.WriteLine($"sharp: {failure}");
        }
    }

    public bool WantsExit => state.ExitRequested;

    public int ExitStatus => state.ExitStatus;

    public string WorkingDirectory => state.WorkingDirectory;

    public int Run(string line, CancellationToken cancellationToken)
    {
        if (line.Trim().Length == 0)
        {
            return state.LastExitCode;
        }

        explaining?.Forget();

        ShellRun run = executor.Run(line, state, cancellationToken);
        Explain(run.Classification);

        output.Write(run.Result.Stdout);
        error.Write(run.Result.Stderr);
        state.LastExitCode = run.Result.ExitCode;

        return run.Result.ExitCode;
    }

    // Strict mode is the sandboxed guest's configuration: no process can be started, so an unowned
    // name is refused at the dispatch point rather than escaped to. It is also what makes this
    // binary testable — with the fall-through on, a corpus case using an unimplemented construct
    // would be answered by a real shell and score as a pass, measuring nothing.
    private static ICommandApprover Policy(SessionSettings settings) =>
        settings.Strict ? new OwnedOnlyCommandApprover() : new AllowAllCommandApprover();

    private void Explain(Classification classification)
    {
        if (!settings.Explains)
        {
            return;
        }

        string tier = classification.Tier == ExecutionTier.Owned ? "owned" : "native";
        string programs = classification.UnownedPrograms.Count == 0
            ? string.Empty
            : $" {string.Join(' ', classification.UnownedPrograms)}";
        string mutates = classification.Mutates ? " mutates" : string.Empty;
        string reason = classification.Reason is null ? string.Empty : $" — {classification.Reason}";

        error.WriteLine($"[{tier}{programs}{mutates}]{reason}");

        foreach (string decision in explaining!.Decisions)
        {
            error.WriteLine(decision);
        }
    }
}
