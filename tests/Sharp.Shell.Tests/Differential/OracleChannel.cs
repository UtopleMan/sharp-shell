namespace Sharp.Shell.Tests.Differential;

// Where an oracle's answer comes from: the installed tool, or a recording of it made where the tool
// was installed. Recording runs the tool and keeps what it said.
public sealed class OracleChannel(bool isLive, OracleCassette? cassette, OracleRecorder? recorder)
{
    public static OracleChannel Live(OracleCassette? cassette, OracleRecorder? recorder) =>
        new(isLive: true, cassette, recorder);

    public static OracleChannel Replaying(OracleCassette? cassette) =>
        new(isLive: false, cassette, recorder: null);

    public bool IsLive => isLive;

    public bool IsAvailable => isLive || cassette is not null;

    public OracleResult Run(
        string tool,
        IReadOnlyList<string> arguments,
        string input,
        string workspace,
        Func<OracleResult> runTool)
    {
        if (!isLive)
        {
            return cassette is null
                ? throw new MissingRecordingException(tool, arguments)
                : cassette.Replay(tool, arguments, input, workspace);
        }

        return recorder is null
            ? runTool()
            : recorder.Record(tool, arguments, input, workspace, runTool);
    }
}
