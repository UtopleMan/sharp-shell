using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sharp.Shell.Tests.Differential;

// One external tool, run against a temporary workspace so its answer can be compared with ours.
// Extracted from SedOracle when awk needed two of them; sed and awk now share the process handling,
// the ten-second cap and the availability check. Where the tool is missing its recorded answers are
// replayed instead, so a machine without gawk still runs the awk suites.
public sealed class ExternalOracle
{
    private readonly IReadOnlyList<string> leadingArguments;

    private readonly OracleChannel channel;

    private ExternalOracle(string name, string? path, IReadOnlyList<string> leadingArguments)
    {
        Name = name;
        Path = path;
        this.leadingArguments = leadingArguments;
        channel = OracleRecordings.Channel(path is not null);
    }

    public string Name { get; }

    public string? Path { get; }

    public bool IsLive => channel.IsLive;

    public bool IsAvailable => channel.IsAvailable;

    public string Version => field ??= DescribeVersion();

    // Windows has none of these tools, so every oracle there replays or, with no cassette, reports
    // itself unavailable and the suites that use one skip.
    public static ExternalOracle Locate(string name, IEnumerable<string> candidatePaths, params string[] leadingArguments)
    {
        string? path = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? null
            : candidatePaths.FirstOrDefault(File.Exists);

        return new ExternalOracle(name, path, leadingArguments);
    }

    public OracleResult Run(IReadOnlyList<string> arguments, string input, string workingDirectory)
    {
        string[] full = [.. leadingArguments, .. arguments];

        return channel.Run(Name, full, input, workingDirectory, () => Execute(full, input, workingDirectory));
    }

    // A call whose recorded answer moved between two live runs cannot be replayed faithfully, so
    // callers drop it rather than compare against a stale answer.
    public bool CanCompare(IReadOnlyList<string> arguments, string input) =>
        IsLive || !OracleRecordings.IsKnownUnstable(Name, [.. leadingArguments, .. arguments], input);

    private OracleResult Execute(IReadOnlyList<string> arguments, string input, string workingDirectory)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path!,
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        WriteInput(process, input);

        // Asynchronous reads keep the ten-second cap honest: a program that never exits and never
        // closes its pipes would otherwise block ReadToEnd before the wait is ever reached.
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        bool exited = process.WaitForExit(milliseconds: 10_000);

        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        return new OracleResult(stdout.Result, stderr.Result, exited ? process.ExitCode : -1);
    }

    // A program that never reads — `awk 'BEGIN {print 1}'` — can exit before the input is written,
    // and the pipe closing under the write is expected rather than a failure.
    private static void WriteInput(Process process, string input)
    {
        try
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
        }
    }

    // Read straight from the process rather than through the channel: the version is reported in
    // test output, and putting it in the cassette would record an answer nothing ever replays.
    private string DescribeVersion()
    {
        if (!IsLive)
        {
            return OracleRecordings.RecordedVersion(Name) ?? "unavailable";
        }

        OracleResult result = Execute([.. leadingArguments, "--version"], string.Empty, System.IO.Path.GetTempPath());
        string reported = result.Stdout.Length > 0 ? result.Stdout : result.Stderr;
        string version = reported.Split('\n')[0].Trim();
        OracleRecordings.NoteVersion(Name, version);

        return version;
    }
}
