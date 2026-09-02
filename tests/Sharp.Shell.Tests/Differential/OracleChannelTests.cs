using Xunit;

namespace Sharp.Shell.Tests.Differential;

public class OracleChannelTests
{
    private static readonly string[] EchoArguments = ["-c", "echo hi"];

    [Fact]
    public void AChannelWithTheToolInstalledRunsIt()
    {
        OracleChannel channel = OracleChannel.Live(cassette: null, recorder: null);

        OracleResult result = channel.Run("bash", EchoArguments, string.Empty, "/tmp/root", () => new OracleResult("live\n", string.Empty, 0));

        Assert.Equal("live\n", result.Stdout);
    }

    [Fact]
    public void AChannelWithoutTheToolReplaysTheCassetteInstead()
    {
        OracleChannel channel = OracleChannel.Replaying(Recorded("recorded\n"));

        OracleResult result = channel.Run("bash", EchoArguments, string.Empty, "/tmp/root", () => throw new InvalidOperationException("the tool is not installed"));

        Assert.Equal("recorded\n", result.Stdout);
    }

    [Fact]
    public void AChannelWithNeitherTheToolNorACassetteIsUnavailable()
    {
        OracleChannel channel = OracleChannel.Replaying(cassette: null);

        Assert.False(channel.IsAvailable);
        Assert.Throws<MissingRecordingException>(
            () => channel.Run("bash", EchoArguments, string.Empty, "/tmp/root", () => new OracleResult(string.Empty, string.Empty, 0)));
    }

    [Fact]
    public void ARecordingChannelRunsTheToolAndKeepsTheAnswer()
    {
        OracleRecorder recorder = new();
        OracleChannel channel = OracleChannel.Live(cassette: null, recorder);

        OracleResult result = channel.Run("bash", EchoArguments, string.Empty, "/tmp/root", () => new OracleResult("live\n", string.Empty, 0));

        Assert.Equal("live\n", result.Stdout);
        Assert.True(recorder.Has("bash", EchoArguments, string.Empty));
    }

    private static OracleCassette Recorded(string stdout)
    {
        OracleRecorder recorder = new();
        recorder.Record("bash", EchoArguments, string.Empty, "/tmp/root", () => new OracleResult(stdout, string.Empty, 0));

        return recorder.ToCassette();
    }
}
