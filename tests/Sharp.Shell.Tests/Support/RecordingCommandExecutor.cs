using Sharp.Shell.Execution;

namespace Sharp.Shell.Tests.Support;

// Records the two requests the shell can make of the process seam, so a test can assert not just
// what a command did but whether a process was ever asked for at all.
internal sealed class RecordingCommandExecutor(string output = "", int exitCode = 0) : ICommandExecutor
{
    public List<string> Commands { get; } = [];

    public List<string> Lines { get; } = [];

    public CommandExecution Execute(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IEnumerable<string> input,
        CancellationToken cancellationToken)
    {
        Commands.Add(CommandText.Of(program, arguments));

        return new CommandExecution(true, exitCode, TextStream.FromText(output), string.Empty);
    }

    public CommandExecution ExecuteLine(
        string commandLine,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        Lines.Add(commandLine);

        return new CommandExecution(true, exitCode, TextStream.FromText(output), string.Empty);
    }
}
