using Sharp.Shell.Execution;

namespace Sharp.Shell;

// What one external program run produced. IsSupported is false when this executor cannot run
// programs at all, which is the guest's answer and the reason the shell never needs fork or exec.
public sealed record CommandExecution(bool IsSupported, int ExitCode, IEnumerable<string> Output, string Error)
{
    public static CommandExecution NotSupported { get; } = new(false, 127, TextStream.Empty, string.Empty);
}

// The process seam, and the only way a process is ever started. Nothing reaches it except through
// ShellExecutor, and never before ICommandApprover has been asked — that is the invariant: a native
// process runs because the shell asked for one, never because the host decided to go native on its
// own. Natively it runs a real process; inside the wasm guest it returns NotSupported.
//
// The shell makes exactly two kinds of request. Execute runs one command the applet registry does
// not own, with its arguments already expanded. ExecuteLine runs a whole line the shell cannot run
// at all — one that will not parse, or that needs the process model the guest has no threads for —
// and is the coarse case: nobody can say what the commands in it are, so it is approved as one
// target or not at all.
//
// Both carry the shell's exported variables. The shell decides *what* is offered to a child; what a
// child actually receives is the host's decision, and filtering it belongs here rather than in the
// shell.
public interface ICommandExecutor
{
    CommandExecution Execute(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        IEnumerable<string> input,
        CancellationToken cancellationToken);

    // Defaulted so an executor that can only run programs — the guest, every test double — keeps
    // the fail-closed answer without writing it out, and the shell reports the line's own reason
    // instead.
    CommandExecution ExecuteLine(
        string commandLine,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken) => CommandExecution.NotSupported;
}

public sealed class NotSupportedCommandExecutor : ICommandExecutor
{
    public CommandExecution Execute(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        IEnumerable<string> input,
        CancellationToken cancellationToken) => CommandExecution.NotSupported;
}
