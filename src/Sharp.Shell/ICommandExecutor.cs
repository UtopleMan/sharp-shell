using Sharp.Shell.Execution;

namespace Sharp.Shell;

// What one external program run produced. IsSupported is false when this executor cannot run
// programs at all, which is the guest's answer and the reason the shell never needs fork or exec.
public sealed record CommandExecution(bool IsSupported, int ExitCode, IEnumerable<string> Output, string Error)
{
    public static CommandExecution NotSupported { get; } = new(false, 127, TextStream.Empty, string.Empty);
}

// The one seam: consulted only for names the applet registry does not own. Natively it runs a real
// process, which is what unlocks the half of the conformance corpus that needs external helpers;
// inside the wasm guest it returns NotSupported.
//
// Classification is the primary mechanism, so in the guest an unowned command is normally diverted
// to the native tier before execution begins and this is never reached. NotSupported is the
// fail-closed backstop for anything classification missed, not the main path.
public interface ICommandExecutor
{
    CommandExecution Execute(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IEnumerable<string> input,
        CancellationToken cancellationToken);
}

public sealed class NotSupportedCommandExecutor : ICommandExecutor
{
    public CommandExecution Execute(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IEnumerable<string> input,
        CancellationToken cancellationToken) => CommandExecution.NotSupported;
}
