using Sharp.Shell.Execution;

namespace Sharp.Shell.Tests.Support;

// A native program that streams: it yields each input chunk as it arrives and counts what it
// actually consumed. A boundary that buffers instead will run the counter away, which is the whole
// point — the assertion is on the counter, not on the output.
internal sealed class StreamingCommandExecutor : ICommandExecutor
{
    public int Consumed { get; private set; }

    public CommandExecution Execute(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        IEnumerable<string> input,
        CancellationToken cancellationToken) =>
        new(true, 0, Echo(input), string.Empty);

    private IEnumerable<string> Echo(IEnumerable<string> input)
    {
        foreach (string chunk in input)
        {
            Consumed++;
            yield return chunk;
        }
    }
}
