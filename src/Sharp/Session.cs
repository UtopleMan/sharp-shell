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
    private readonly ShellExecutor executor = new(AppletRegistry.CreateDefault(), new NotSupportedCommandExecutor());
    private readonly ShellState state;
    private readonly bool explains;
    private readonly bool strict;

    // ShellState starts at its root, which for an unconfined session is "/" — not where the user
    // ran the shell. A shell that opens somewhere other than your current directory is useless, so
    // the session moves there first.
    public Session(string rootPath, string startDirectory, bool explains, bool strict)
    {
        this.explains = explains;
        this.strict = strict;
        state = new ShellState(rootPath);

        if (!state.TryChangeDirectory(startDirectory, out string error))
        {
            Console.Error.WriteLine($"sharp: {error}");
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

        ShellRun run = executor.Run(line, state, cancellationToken);
        Explain(run.Classification);

        if (run.Result is { } result)
        {
            Console.Out.Write(result.Stdout);
            Console.Error.Write(result.Stderr);
            state.LastExitCode = result.ExitCode;

            return result.ExitCode;
        }

        // Nothing ran: classification said this line belongs to a real shell.
        //
        // In strict mode there is no real shell to hand it to. That is what makes this binary
        // testable: with the fall-through enabled, a corpus case using an unimplemented construct
        // would be answered by bash and score as a pass, measuring nothing. Strict mode is also
        // exactly the configuration the sandboxed guest runs in, where no process can be started
        // at all.
        if (strict)
        {
            Console.Error.WriteLine($"sharp: {run.Classification.Reason ?? "not an owned command"}");
            state.LastExitCode = 127;
            return state.LastExitCode;
        }

        state.LastExitCode = NativeTier.Run(line, state.WorkingDirectory);
        return state.LastExitCode;
    }

    private void Explain(Classification classification)
    {
        if (!explains)
        {
            return;
        }

        string tier = classification.Tier == ExecutionTier.Owned ? "owned" : "native";
        string programs = classification.UnownedPrograms.Count == 0
            ? string.Empty
            : $" {string.Join(' ', classification.UnownedPrograms)}";
        string mutates = classification.Mutates ? " mutates" : string.Empty;
        string reason = classification.Reason is null ? string.Empty : $" — {classification.Reason}";

        Console.Error.WriteLine($"[{tier}{programs}{mutates}]{reason}");
    }
}
