using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// A child's exit code and stderr are not knowable until its output has been read, so an executor
// that streams reports them when the stream ends. An implementation that reads them up front makes
// every native command wait for its child to exit, which is exactly the buffering these tests are
// here to keep out.
public class StreamingExecutorTests
{
    [Fact]
    public void AStreamingCommandsExitCodeIsReadWhenItsOutputEnds()
    {
        DeferredCommandExecutor external = new(3, string.Empty, ["a\n"]);
        using ShellHarness harness = new(external);

        ShellResult result = harness.Run("native-thing && echo ran");

        Assert.Equal("a\n", result.Stdout);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void AStreamingCommandsErrorIsWrittenWhenItsOutputEnds()
    {
        DeferredCommandExecutor external = new(1, "native-thing: no such file\n", ["a\n"]);
        using ShellHarness harness = new(external);

        ShellResult result = harness.Run("native-thing");

        Assert.Equal("native-thing: no such file\n", result.Stderr);
    }

    // The native half of SIGPIPE. An endless producer that only stops because its consumer did is
    // the only assertion that cannot pass by buffering: buffering never returns.
    [Fact]
    public void AFinishedConsumerEndsTheNativeProducer()
    {
        DeferredCommandExecutor external = new(0, string.Empty, DeferredCommandExecutor.Endless());
        using ShellHarness harness = new(external);

        ShellResult result = harness.Run("native-thing | head -2");

        Assert.Equal("line 1\nline 2\n", result.Stdout);
        Assert.True(external.Produced <= 3, $"the native stage produced {external.Produced} lines for a head -2");
        Assert.True(external.HasEnded, "the native stage was never told its consumer had finished");
    }
}
