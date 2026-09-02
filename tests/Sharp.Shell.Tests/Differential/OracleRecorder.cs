using System.Text.Json;

namespace Sharp.Shell.Tests.Differential;

// Collects live oracle answers so they can be replayed on a machine without the tools.
public sealed class OracleRecorder
{
    private readonly Lock guard = new();

    private readonly Dictionary<string, OracleRecording> captured = [];

    public OracleResult Record(
        string tool,
        IReadOnlyList<string> arguments,
        string input,
        string workspace,
        Func<OracleResult> run)
    {
        OracleResult result = run();
        OracleResult confirmation = run();

        OracleRecording recording = new(
            tool,
            [.. arguments],
            input,
            Generalise(result.Stdout, workspace),
            Generalise(result.Stderr, workspace),
            result.ExitCode,
            Unstable: result != confirmation);

        lock (guard)
        {
            captured[OracleCassette.Key(tool, arguments, input)] = recording;
        }

        return result;
    }

    public bool Has(string tool, IReadOnlyList<string> arguments, string input)
    {
        lock (guard)
        {
            return captured.ContainsKey(OracleCassette.Key(tool, arguments, input));
        }
    }

    public OracleCassette ToCassette() =>
        new(Document(toolVersions: new Dictionary<string, string>(), recordedOn: "in memory"));

    public void Write(string path, IReadOnlyDictionary<string, string> toolVersions, string recordedOn) =>
        File.WriteAllText(path, JsonSerializer.Serialize(Document(toolVersions, recordedOn), SerializerOptions));

    private CassetteDocument Document(IReadOnlyDictionary<string, string> toolVersions, string recordedOn)
    {
        lock (guard)
        {
            return new CassetteDocument(
                recordedOn,
                toolVersions.ToDictionary(version => version.Key, version => version.Value),
                [.. captured.Values.OrderBy(entry => entry.Tool, StringComparer.Ordinal)
                    .ThenBy(entry => string.Join(' ', entry.Arguments), StringComparer.Ordinal)]);
        }
    }

    private static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // The workspace is a fresh temp directory on every run, so a recording that quoted the one it
    // was made in would never match again. The cassette puts today's path back at replay.
    private static string Generalise(string output, string workspace) =>
        output.Replace(workspace, OracleCassette.WorkspacePlaceholder, StringComparison.Ordinal);
}
