using Sharp.Shell.Commands;
using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// A caller that runs shell text on behalf of a command has a stdin to pass on. Without the overload
// that takes one, `echo hi | bash script.sh` reaches the body with nothing to read and the pipe is
// silently lost.
public class ExternalInputTests
{
    [Fact]
    public void TextRunWithAnInputReadsIt()
    {
        string root = Directory.CreateTempSubdirectory("duetui-shell-input").FullName;

        try
        {
            ShellResult result = Executor().Run("cat", new ShellState(root), ["piped\n"], CancellationToken.None).Result;

            Assert.Equal("piped\n", result.Stdout);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ShellExecutor Executor() =>
        new(AppletRegistry.CreateDefault(), new NotSupportedCommandExecutor());
}
