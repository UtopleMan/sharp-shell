using Sharp.Shell.Execution;

namespace Sharp.Shell.Tests.Support;

internal sealed class StubCommandExecutor(string output, int exitCode) : ICommandExecutor
{
    public CommandExecution Execute(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IEnumerable<string> input,
        CancellationToken cancellationToken) =>
        new(true, exitCode, TextStream.FromText(output), string.Empty);
}
