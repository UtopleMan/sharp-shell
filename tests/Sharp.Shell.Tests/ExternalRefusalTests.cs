using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// An executor can refuse too. It happens when the thing it dispatches to is itself a shell — a
// script body whose commands are approved one at a time — and a denial down there has to unwind the
// run up here. Reporting it as a non-zero exit code would leave `denied || fallback` free to route
// around an answer the host already gave, which is the hole RefusalTests exists to keep shut.
public class ExternalRefusalTests
{
    [Fact]
    public void ARefusedExternalCommandUnwindsTheRun()
    {
        using ShellHarness harness = new(Refusing("deploy.sh: git push: refused"));

        ShellResult result = harness.Run("native-thing || echo fallback");

        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(126, result.ExitCode);
        Assert.Equal("deploy.sh: git push: refused", result.RefusalReason);
    }

    [Fact]
    public void ARefusedExternalCommandStopsTheCommandsAfterIt()
    {
        using ShellHarness harness = new(Refusing("refused"));

        ShellResult result = harness.Run("native-thing; echo after");

        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(126, result.ExitCode);
    }

    [Fact]
    public void AnExternalCommandThatDidNotRefuseIsUnaffected()
    {
        using ShellHarness harness = new(new StubCommandExecutor("ran\n", 0));

        ShellResult result = harness.Run("native-thing || echo fallback");

        Assert.Equal("ran\n", result.Stdout);
        Assert.Null(result.RefusalReason);
    }

    private static ICommandExecutor Refusing(string reason) => new RefusingCommandExecutor(reason);

    private sealed class RefusingCommandExecutor(string reason) : ICommandExecutor
    {
        public CommandExecution Execute(
            string program,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environment,
            IEnumerable<string> input,
            CancellationToken cancellationToken) =>
            new(true, 126, TextStream.Empty, string.Empty) { RefusalReason = reason };
    }
}
