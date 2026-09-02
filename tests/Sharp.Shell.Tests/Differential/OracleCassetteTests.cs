using Xunit;

namespace Sharp.Shell.Tests.Differential;

public class OracleCassetteTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("sharp-cassette").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void ReplayAnswersWithTheRecordedResult()
    {
        OracleCassette cassette = Load("""
            {
              "recordedOn": "Darwin 25.6.0",
              "tools": { "bash": "5.2.37(1)-release" },
              "entries": [
                {
                  "tool": "bash",
                  "arguments": ["--norc", "--noprofile", "-c", "echo hello"],
                  "input": "",
                  "stdout": "hello\n",
                  "stderr": "",
                  "exitCode": 0
                }
              ]
            }
            """);

        OracleResult result = cassette.Replay("bash", ["--norc", "--noprofile", "-c", "echo hello"], string.Empty, "/tmp/workspace");

        Assert.Equal("hello\n", result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ReplayingACallThatWasNeverRecordedSaysHowToRecordIt()
    {
        OracleCassette cassette = Load("""
            {
              "recordedOn": "Darwin 25.6.0",
              "tools": { "bash": "5.2.37(1)-release" },
              "entries": [
                {
                  "tool": "bash",
                  "arguments": ["-c", "echo hello"],
                  "input": "",
                  "stdout": "hello\n",
                  "stderr": "",
                  "exitCode": 0
                }
              ]
            }
            """);

        MissingRecordingException failure = Assert.Throws<MissingRecordingException>(
            () => cassette.Replay("bash", ["-c", "echo unrecorded"], string.Empty, "/tmp/workspace"));

        Assert.Contains("echo unrecorded", failure.Message, StringComparison.Ordinal);
        Assert.Contains("SHARP_ORACLE_RECORD=1", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayPutsTheLiveWorkspaceWhereTheRecordedOneWas()
    {
        OracleCassette cassette = Load("""
            {
              "recordedOn": "Darwin 25.6.0",
              "tools": { "bash": "5.2.37(1)-release" },
              "entries": [
                {
                  "tool": "bash",
                  "arguments": ["-c", "pwd; ls missing"],
                  "input": "",
                  "stdout": "{{workspace}}\n",
                  "stderr": "ls: {{workspace}}/missing: No such file or directory\n",
                  "exitCode": 1
                }
              ]
            }
            """);

        OracleResult result = cassette.Replay("bash", ["-c", "pwd; ls missing"], string.Empty, "/tmp/live-root");

        Assert.Equal("/tmp/live-root\n", result.Stdout);
        Assert.Equal("ls: /tmp/live-root/missing: No such file or directory\n", result.Stderr);
    }

    [Fact]
    public void ARecordedCallReplaysFromTheWrittenCassette()
    {
        OracleRecorder recorder = new();
        recorder.Record("bash", ["-c", "echo hi"], string.Empty, "/tmp/recording-root", () => new OracleResult("hi\n", string.Empty, 0));

        OracleCassette cassette = WriteAndLoad(recorder);
        OracleResult replayed = cassette.Replay("bash", ["-c", "echo hi"], string.Empty, "/tmp/replay-root");

        Assert.Equal("hi\n", replayed.Stdout);
        Assert.Equal(0, replayed.ExitCode);
    }

    [Fact]
    public void RecordingKeepsTheWorkspacePathOutOfTheCassette()
    {
        OracleRecorder recorder = new();
        recorder.Record(
            "bash",
            ["-c", "pwd"],
            string.Empty,
            "/tmp/recording-root",
            () => new OracleResult("/tmp/recording-root\n", string.Empty, 0));

        OracleCassette cassette = WriteAndLoad(recorder);
        OracleResult replayed = cassette.Replay("bash", ["-c", "pwd"], string.Empty, "/tmp/replay-root");

        Assert.Equal("/tmp/replay-root\n", replayed.Stdout);
    }

    [Fact]
    public void ACallWhoseAnswerChangesBetweenRunsIsMarkedUnstable()
    {
        int runs = 0;
        OracleRecorder recorder = new();
        recorder.Record("bash", ["-c", "date +%s%N"], string.Empty, "/tmp/recording-root", () =>
        {
            runs++;
            return new OracleResult($"{runs}\n", string.Empty, 0);
        });

        OracleCassette cassette = WriteAndLoad(recorder);

        Assert.True(cassette.IsUnstable("bash", ["-c", "date +%s%N"], string.Empty));
    }

    [Fact]
    public void ACallThatAnswersTheSameTwiceIsNotMarkedUnstable()
    {
        OracleRecorder recorder = new();
        recorder.Record("bash", ["-c", "echo hi"], string.Empty, "/tmp/recording-root", () => new OracleResult("hi\n", string.Empty, 0));

        OracleCassette cassette = WriteAndLoad(recorder);

        Assert.False(cassette.IsUnstable("bash", ["-c", "echo hi"], string.Empty));
    }

    [Fact]
    public void TheCassetteReportsTheVersionOfTheToolItRecorded()
    {
        OracleCassette cassette = Load("""
            {
              "recordedOn": "Darwin 25.6.0",
              "tools": { "bash": "5.2.37(1)-release" },
              "entries": []
            }
            """);

        Assert.Equal("5.2.37(1)-release (recorded on Darwin 25.6.0)", cassette.VersionOf("bash"));
        Assert.Null(cassette.VersionOf("zsh"));
    }

    private OracleCassette WriteAndLoad(OracleRecorder recorder)
    {
        string path = Path.Combine(directory, "written.json");
        recorder.Write(path, new Dictionary<string, string> { ["bash"] = "5.2.37(1)-release" }, "Darwin 25.6.0");

        return OracleCassette.Load(path);
    }

    private OracleCassette Load(string json)
    {
        string path = Path.Combine(directory, "oracle-recordings.json");
        File.WriteAllText(path, json);

        return OracleCassette.Load(path);
    }
}
