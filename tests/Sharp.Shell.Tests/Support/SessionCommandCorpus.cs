using System.Text.Json;

namespace Sharp.Shell.Tests.Support;

// The bash commands this project's own agent has actually run, mined from the stored sessions in
// ~/.duetui/sessions. This is the distribution that decides whether the sandbox is worth having,
// and no public corpus covers it.
//
// Generated at test time from the local machine and never committed: these command strings carry
// real paths and can carry secrets.
public static class SessionCommandCorpus
{
    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".duetui",
            "sessions");

    // A missing directory yields nothing rather than failing, so a clean machine still runs green.
    public static IReadOnlyList<string> Commands()
    {
        if (!Directory.Exists(DefaultDirectory))
        {
            return [];
        }

        List<string> commands = [];

        foreach (string file in Directory.EnumerateFiles(DefaultDirectory, "*.jsonl"))
        {
            commands.AddRange(ReadFile(file));
        }

        return [.. commands.Distinct(StringComparer.Ordinal)];
    }

    private static IEnumerable<string> ReadFile(string file)
    {
        foreach (string line in ReadLines(file))
        {
            if (TryReadCommand(line, out string command))
            {
                yield return command;
            }
        }
    }

    private static IEnumerable<string> ReadLines(string file)
    {
        try
        {
            return File.ReadAllLines(file);
        }
        catch (IOException)
        {
            return [];
        }
    }

    // A ToolCall entry stores its arguments as a JSON *string* holding a JSON object, so the
    // payload is parsed twice.
    private static bool TryReadCommand(string line, out string command)
    {
        command = string.Empty;

        try
        {
            using JsonDocument entry = JsonDocument.Parse(line);

            if (!Matches(entry.RootElement, "kind", "ToolCall") || !Matches(entry.RootElement, "toolName", "bash"))
            {
                return false;
            }

            if (!entry.RootElement.TryGetProperty("arguments", out JsonElement arguments)
                || arguments.GetString() is not { } payload)
            {
                return false;
            }

            using JsonDocument parsed = JsonDocument.Parse(payload);

            if (!parsed.RootElement.TryGetProperty("command", out JsonElement value)
                || value.GetString() is not { Length: > 0 } text)
            {
                return false;
            }

            command = text;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool Matches(JsonElement element, string property, string expected) =>
        element.TryGetProperty(property, out JsonElement value)
        && string.Equals(value.GetString(), expected, StringComparison.Ordinal);
}
