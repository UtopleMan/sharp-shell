using Sharp.Shell.Execution;

namespace Sharp.Shell.Tests.Support;

// Records the two requests the shell can make of the process seam, so a test can assert not just
// what a command did but whether a process was ever asked for at all.
internal sealed class RecordingCommandExecutor(string output = "", int exitCode = 0) : ICommandExecutor
{
    public List<string> Commands { get; } = [];

    public List<string> Lines { get; } = [];

    // What each request was offered as the child's environment, so a test can assert that an
    // unexported variable never left the shell.
    public List<IReadOnlyDictionary<string, string>> Environments { get; } = [];

    public List<IReadOnlyDictionary<string, string>> LineEnvironments { get; } = [];

    public CommandExecution Execute(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        IEnumerable<string> input,
        CancellationToken cancellationToken)
    {
        Commands.Add(CommandText.Of(program, arguments));
        Environments.Add(environment);

        return new CommandExecution(true, exitCode, TextStream.FromText(output), string.Empty);
    }

    public CommandExecution ExecuteLine(
        string commandLine,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        Lines.Add(commandLine);
        LineEnvironments.Add(environment);

        return new CommandExecution(true, exitCode, TextStream.FromText(output), string.Empty);
    }
}
