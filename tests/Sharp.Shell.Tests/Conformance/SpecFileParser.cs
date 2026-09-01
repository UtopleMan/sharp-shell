using System.Text;
using System.Text.Json;

namespace Sharp.Shell.Tests.Conformance;

// Parses the oils spec format. The directive semantics below were read off the vendored corpus
// (spec/word-split.test.sh, spec/alias.test.sh, spec/redirect.test.sh), not assumed:
//
//   #### <description>          starts a case; its body runs until the first directive
//   ## STDOUT:  … ## END        multi-line expected stdout (## END: also appears, 8 times)
//   ## stdout: <text>           single-line expected stdout, with a trailing newline implied
//   ## stdout-json: <json>      expected stdout as a JSON string, so exact bytes are expressible
//   ## STDERR: … ## END         multi-line expected stderr, with the same lowercase variants
//   ## status: <n>              expected exit status
//   ## OK <shells> <key>…       those shells legitimately differ; this is their expectation
//   ## BUG <shells> <key>…      those shells are known-buggy; this is still what they do
//   ## N-I <shells> <key>…      not implemented there; this is what they do instead
//   ## OK-2 / OK-3 / OK-4 / BUG-2   further alternatives, all equally acceptable
//   ## compare_shells: / ## our_shell: / ## tags: / ## suite: / ## code: /
//   ## oils_failures_allowed: / ## oils_cpp_failures_allowed: / ## legacy_tmp_dir:   metadata
//
// Shell lists are slash-separated (dash/ash) and may be version-pinned (bash-4.4). This project
// emulates bash, so the first bash-qualified block *replaces* the unqualified expectation and any
// further bash-qualified blocks are added as alternatives; blocks naming only other shells are
// ignored.
//
// An unrecognised directive marks its case Unparsed. Those are counted and asserted to be zero —
// a directive this parser does not understand is a gap here, never a pass.
public static class SpecFileParser
{
    private const string EmulatedShell = "bash";

    private static readonly string[] Qualifiers = ["OK", "OK-2", "OK-3", "OK-4", "BUG", "BUG-2", "N-I", "N-I-2"];

    private static readonly string[] Metadata =
    [
        "compare_shells", "oils_failures_allowed", "oils_cpp_failures_allowed", "tags", "suite",
        "code", "our_shell", "flaky", "legacy_tmp_dir",
    ];

    public static IReadOnlyList<SpecCase> Parse(string fileName, string text)
    {
        List<SpecCase> cases = [];
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        int index = 0;

        while (index < lines.Length)
        {
            if (!lines[index].StartsWith("#### ", StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            cases.Add(ParseCase(fileName, cases.Count, lines, ref index));
        }

        return cases;
    }

    // A file declares which shells it is written against; only those including bash are ours.
    // "bash-4.4" pins a version and still means bash.
    public static bool ComparesAgainstBash(string text) =>
        text.Replace("\r\n", "\n")
            .Split('\n')
            .Where(line => line.StartsWith("## compare_shells:", StringComparison.Ordinal))
            .Any(line => line["## compare_shells:".Length..]
                .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Any(IsEmulatedShell));

    private static bool IsEmulatedShell(string token) => token.StartsWith(EmulatedShell, StringComparison.Ordinal);

    private static SpecCase ParseCase(string fileName, int caseIndex, string[] lines, ref int index)
    {
        string description = lines[index]["#### ".Length..].Trim();
        index++;

        StringBuilder body = new();
        while (index < lines.Length && !IsDirective(lines[index]) && !lines[index].StartsWith("#### ", StringComparison.Ordinal))
        {
            body.Append(lines[index]).Append('\n');
            index++;
        }

        Expectations expectations = new();
        while (index < lines.Length && !lines[index].StartsWith("#### ", StringComparison.Ordinal))
        {
            if (!IsDirective(lines[index]))
            {
                index++;
                continue;
            }

            ReadDirective(lines, ref index, expectations);
        }

        return new SpecCase(
            fileName,
            caseIndex,
            description,
            body.ToString(),
            expectations.Stdout,
            expectations.Stderr,
            expectations.Status,
            expectations.UnparsedDirective is not null,
            expectations.UnparsedDirective);
    }

    private static bool IsDirective(string line) =>
        line.StartsWith("## ", StringComparison.Ordinal) || line == "## END" || line == "## END:";

    private static void ReadDirective(string[] lines, ref int index, Expectations expectations)
    {
        string line = lines[index];
        index++;

        if (line is "## END" or "## END:")
        {
            return;
        }

        string content = line["## ".Length..];
        string head = content.Split(' ')[0].TrimEnd(':');

        if (Metadata.Contains(head, StringComparer.Ordinal) || head.Contains(':', StringComparison.Ordinal))
        {
            return;
        }

        if (Qualifiers.Contains(head, StringComparer.Ordinal))
        {
            ReadQualified(content, lines, ref index, expectations);
            return;
        }

        Apply(content, lines, ref index, expectations, applies: true, qualified: false);
    }

    private static void ReadQualified(string content, string[] lines, ref int index, Expectations expectations)
    {
        string[] pieces = content.Split(' ', 3);
        if (pieces.Length < 3)
        {
            expectations.UnparsedDirective ??= content;
            return;
        }

        bool appliesToBash = pieces[1].Split('/').Any(IsEmulatedShell);
        Apply(pieces[2], lines, ref index, expectations, appliesToBash, qualified: true);
    }

    private static void Apply(
        string content,
        string[] lines,
        ref int index,
        Expectations expectations,
        bool applies,
        bool qualified)
    {
        int colon = content.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            expectations.UnparsedDirective ??= content;
            return;
        }

        string key = content[..colon];
        string inline = content[(colon + 1)..].TrimStart();

        switch (key)
        {
            case "STDOUT":
                expectations.AddStdout(ReadBlock(lines, ref index), applies, qualified);
                return;
            case "STDERR":
                expectations.AddStderr(ReadBlock(lines, ref index), applies, qualified);
                return;
            case "stdout":
                expectations.AddStdout(inline + "\n", applies, qualified);
                return;
            case "stderr":
                expectations.AddStderr(inline + "\n", applies, qualified);
                return;
            case "stdout-json":
                expectations.AddStdout(DecodeJson(inline), applies, qualified);
                return;
            case "stderr-json":
                expectations.AddStderr(DecodeJson(inline), applies, qualified);
                return;
            case "status":
                expectations.AddStatus(inline, applies, qualified);
                return;
            default:
                expectations.UnparsedDirective ??= content;
                return;
        }
    }

    private static string ReadBlock(string[] lines, ref int index)
    {
        StringBuilder block = new();

        while (index < lines.Length && lines[index] is not ("## END" or "## END:"))
        {
            block.Append(lines[index]).Append('\n');
            index++;
        }

        if (index < lines.Length)
        {
            index++;
        }

        return block.ToString();
    }

    private static string DecodeJson(string inline)
    {
        try
        {
            return JsonSerializer.Deserialize<string>(inline) ?? string.Empty;
        }
        catch (JsonException)
        {
            return inline;
        }
    }

    // The first bash-qualified value replaces whatever the unqualified directive said, because it
    // exists precisely to say "bash differs here". Later bash-qualified values are alternatives.
    private sealed class Expectations
    {
        private readonly List<string> stdout = [];
        private readonly List<string> stderr = [];
        private readonly List<int> status = [];

        private bool stdoutOverridden;
        private bool stderrOverridden;
        private bool statusOverridden;

        public IReadOnlyList<string> Stdout => stdout;

        public IReadOnlyList<string> Stderr => stderr;

        public IReadOnlyList<int> Status => status;

        public string? UnparsedDirective { get; set; }

        public void AddStdout(string value, bool applies, bool qualified) =>
            Add(stdout, value, applies, qualified, ref stdoutOverridden);

        public void AddStderr(string value, bool applies, bool qualified) =>
            Add(stderr, value, applies, qualified, ref stderrOverridden);

        public void AddStatus(string inline, bool applies, bool qualified)
        {
            if (!int.TryParse(inline, out int value))
            {
                return;
            }

            Add(status, value, applies, qualified, ref statusOverridden);
        }

        private static void Add<T>(List<T> values, T value, bool applies, bool qualified, ref bool overridden)
        {
            if (!applies)
            {
                return;
            }

            if (qualified && !overridden)
            {
                values.Clear();
                overridden = true;
            }

            values.Add(value);
        }
    }
}
