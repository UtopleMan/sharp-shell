using Sharp.Shell;
using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;

namespace Sharp.Shell.Tests.Conformance;

// The corpus calls four tiny python2 helpers. They are reimplemented in C# (see SpecHelpers), so
// the conformance run needs no external process and no python. Anything else is refused, which is
// exactly what the wasm guest's executor does.
internal sealed class CorpusCommandExecutor : ICommandExecutor
{
    public CommandExecution Execute(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IEnumerable<string> input,
        CancellationToken cancellationToken) =>
        SpecHelpers.TryRun(program, arguments, out CommandExecution execution)
            ? execution
            : CommandExecution.NotSupported;
}
