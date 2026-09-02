using System.Text.Json;

namespace Sharp.Shell.Tests.Differential;

public sealed record OracleRecording(
    string Tool,
    string[] Arguments,
    string Input,
    string Stdout,
    string Stderr,
    int ExitCode,
    bool Unstable = false);

public sealed record CassetteDocument(
    string RecordedOn,
    Dictionary<string, string> Tools,
    OracleRecording[] Entries);

// A call the cassette cannot answer is a hole in the recording, never a pass: the suite that asked
// for it would otherwise compare our shell against nothing.
public sealed class MissingRecordingException(string tool, IReadOnlyList<string> arguments)
    : Exception($"no recorded {tool} answer for [{string.Join(" ", arguments)}]. " +
        "Re-record on a machine that has the tool: SHARP_ORACLE_RECORD=1 dotnet test");

// Answers for oracle calls recorded on a machine that had the real tools, so the differential
// suites can run where bash, sed or awk are absent.
public sealed class OracleCassette(CassetteDocument document)
{
    // A NUL separator rather than a space: several recorded calls pass one argument that itself
    // contains spaces, and joining on a space would make two different calls share a key.
    public const string WorkspacePlaceholder = "{{workspace}}";

    private const char KeySeparator = (char)0;

    private readonly Dictionary<string, OracleRecording> recordings =
        document.Entries.ToDictionary(entry => Key(entry.Tool, entry.Arguments, entry.Input));

    public static OracleCassette Load(string path)
    {
        CassetteDocument document = JsonSerializer.Deserialize<CassetteDocument>(
            File.ReadAllText(path),
            SerializerOptions)!;

        return new OracleCassette(document);
    }

    // Says which tool answered, and where, so a suite reporting "bash 5.2.37" is not read as a claim
    // that this machine ran bash at all.
    public string? VersionOf(string tool) =>
        document.Tools.TryGetValue(tool, out string? version)
            ? $"{version} (recorded on {document.RecordedOn})"
            : null;

    // A command whose own answer moved between two live runs — a clock, a random, a pid — can never
    // be replayed faithfully. The suites drop these from the sample rather than compare our shell
    // against a stale answer.
    public bool IsUnstable(string tool, IReadOnlyList<string> arguments, string input) =>
        recordings.TryGetValue(Key(tool, arguments, input), out OracleRecording? recording) && recording.Unstable;

    public OracleResult Replay(string tool, IReadOnlyList<string> arguments, string input, string workspace)
    {
        if (!recordings.TryGetValue(Key(tool, arguments, input), out OracleRecording? recording))
        {
            throw new MissingRecordingException(tool, arguments);
        }

        return new OracleResult(
            Localise(recording.Stdout, workspace),
            Localise(recording.Stderr, workspace),
            recording.ExitCode);
    }

    // Each run gets a fresh temp workspace, so a recording that quoted the one it was made in would
    // never match again. The recorder writes the placeholder; this puts today's path back.
    private static string Localise(string recorded, string workspace) =>
        recorded.Replace(WorkspacePlaceholder, workspace, StringComparison.Ordinal);

    private static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string Key(string tool, IReadOnlyList<string> arguments, string input) =>
        string.Join(KeySeparator, [tool, .. arguments, input]);
}
