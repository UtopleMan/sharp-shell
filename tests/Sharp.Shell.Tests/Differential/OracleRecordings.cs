using System.Runtime.InteropServices;

namespace Sharp.Shell.Tests.Differential;

// Where the differential suites get their oracle answers. The installed tool wins; a committed
// cassette stands in where the tool is missing, which is every Windows agent and any machine
// without gawk.
//
// Record with SHARP_ORACLE_RECORD=1 on a machine that has the tools; check the cassette is complete
// with SHARP_ORACLE_REPLAY=1, which replays even where the tools are installed.
public static class OracleRecordings
{
    private const string CassetteName = "oracle-recordings.json";

    private const string SourceRelativePath = "tests/Sharp.Shell.Tests/corpus/" + CassetteName;

    private static readonly Lock guard = new();

    private static readonly Dictionary<string, string> liveVersions = [];

    static OracleRecordings()
    {
        if (!IsRecording)
        {
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Write();
    }

    public static bool IsRecording { get; } =
        Environment.GetEnvironmentVariable("SHARP_ORACLE_RECORD") == "1";

    public static bool IsReplayForced { get; } =
        Environment.GetEnvironmentVariable("SHARP_ORACLE_REPLAY") == "1";

    public static OracleChannel Channel(bool isToolInstalled) =>
        isToolInstalled && !IsReplayForced
            ? OracleChannel.Live(Cassette, Recorder)
            : OracleChannel.Replaying(Cassette);

    public static void NoteVersion(string tool, string version)
    {
        lock (guard)
        {
            liveVersions[tool] = version;
        }
    }

    public static string? RecordedVersion(string tool) => Cassette?.VersionOf(tool);

    public static bool IsKnownUnstable(string tool, IReadOnlyList<string> arguments, string input) =>
        Cassette?.IsUnstable(tool, arguments, input) ?? false;

    private static OracleCassette? Cassette { get; } = LoadCassette();

    private static OracleRecorder? Recorder { get; } = IsRecording ? new OracleRecorder() : null;

    private static OracleCassette? LoadCassette()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "corpus", CassetteName);

        return File.Exists(path) ? OracleCassette.Load(path) : null;
    }

    private static void Write()
    {
        if (SourcePath() is not { } path)
        {
            return;
        }

        lock (guard)
        {
            Recorder!.Write(path, liveVersions, RuntimeInformation.OSDescription);
        }
    }

    private static string? SourcePath()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, SourceRelativePath);
            if (Directory.Exists(Path.GetDirectoryName(candidate)!))
            {
                return candidate;
            }
        }

        return null;
    }
}
