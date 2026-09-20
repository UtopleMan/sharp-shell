using Sharp.Shell.Execution;

namespace Sharp.Shell.Tests.Support;

// A native program that streams: it yields a chunk at a time and only knows how it ended once the
// last one has been taken. Nothing here is available up front, which is the point — an executor
// that had to report its exit code before its output was read would have to wait for the child,
// and waiting for the child is what throws the stream away.
internal sealed class DeferredCommandExecutor(int exitCode, string error, IEnumerable<string> chunks)
    : ICommandExecutor
{
    public int Produced { get; private set; }

    public bool HasEnded { get; private set; }

    public CommandExecution Execute(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IEnumerable<string> input,
        CancellationToken cancellationToken)
    {
        CommandExecution execution = new(true, 0, TextStream.Empty, string.Empty);
        execution.Output = Stream(execution);

        return execution;
    }

    private IEnumerable<string> Stream(CommandExecution execution)
    {
        try
        {
            foreach (string chunk in chunks)
            {
                Produced++;
                yield return chunk;
            }
        }
        finally
        {
            HasEnded = true;
            execution.ExitCode = exitCode;
            execution.Error = error;
        }
    }

    public static IEnumerable<string> Endless()
    {
        int line = 1;

        while (true)
        {
            yield return $"line {line++}\n";
        }
    }
}
