using Sharp.Shell.Execution;
using Sharp.Shell.Tests.Support;
using Xunit;

namespace Sharp.Shell.Tests;

// The in-process analogue of SIGPIPE. Without it `find . | head -5` walks the whole tree and
// `yes | head -1` never returns, so this asserts on the producer's counter, not on the output:
// an implementation that buffers everything and then truncates passes the output check and
// fails this one.
public class PipelineStopTests
{
    [Fact]
    public void AFinishedConsumerStopsTheProducer()
    {
        using ShellHarness harness = new();
        CountingApplet counting = new();
        harness.Add(counting);

        ShellResult result = harness.Run("counting | head -3");

        Assert.Equal("line 1\nline 2\nline 3\n", result.Stdout);
        Assert.True(counting.Produced <= 4, $"the producer yielded {counting.Produced} lines for a head -3");
    }

    // The same property across the process seam, in both directions at once: an owned producer
    // feeds a native consumer, whose output feeds an owned consumer that stops early. An executor
    // that drained its input, or a boundary that buffered its output, runs the counters away.
    [Fact]
    public void AFinishedConsumerStopsTheProducerAcrossTheNativeBoundary()
    {
        StreamingCommandExecutor external = new();
        using ShellHarness harness = new(external);
        CountingApplet counting = new();
        harness.Add(counting);

        ShellResult result = harness.Run("counting | native-cat | head -3");

        Assert.Equal("line 1\nline 2\nline 3\n", result.Stdout);
        Assert.True(counting.Produced <= 4, $"the producer yielded {counting.Produced} lines for a head -3");
        Assert.True(external.Consumed <= 4, $"the native stage consumed {external.Consumed} lines for a head -3");
    }
}
