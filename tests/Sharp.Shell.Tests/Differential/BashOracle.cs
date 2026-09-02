using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sharp.Shell.Tests.Differential;

public sealed record OracleResult(string Stdout, string Stderr, int ExitCode);

// Runs a command through the system bash so its answer can be compared with ours. Where bash is
// absent — every Windows agent — the answer comes from the committed cassette instead, and the
// suite only skips when there is neither.
public static class BashOracle
{
    public const string Tool = "bash";

    public static string? Path { get; } = Locate();

    public static bool IsLive => Channel.IsLive;

    public static bool IsAvailable => Channel.IsAvailable;

    public static string Version => field ??= DescribeVersion();

    public static OracleResult Run(string command, string workingDirectory) =>
        Channel.Run(Tool, Arguments(command), string.Empty, workingDirectory, () => Execute(command, workingDirectory));

    // For commands that are mined from the machine running the suite: they cannot be replayed
    // anywhere else, so recording them would only bloat the cassette with one machine's history.
    public static OracleResult RunLive(string command, string workingDirectory) =>
        Execute(command, workingDirectory);

    // A command whose recorded answer moved between two live runs — a clock, a pid — cannot be
    // replayed faithfully, so callers drop it from their sample rather than compare against a stale
    // answer. Live, everything is comparable.
    public static bool CanCompare(string command) =>
        IsLive || !OracleRecordings.IsKnownUnstable(Tool, Arguments(command), string.Empty);

    private static OracleChannel Channel { get; } = OracleRecordings.Channel(Path is not null);

    private static string[] Arguments(string command) => ["--norc", "--noprofile", "-c", command];

    private static OracleResult Execute(string command, string workingDirectory)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path!,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        foreach (string argument in Arguments(command))
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();

        // The streams are read asynchronously so the ten-second cap is reached even when the
        // command never exits — a mined `while true; do :; done` once held the whole suite for
        // hours on a synchronous ReadToEnd that sat in front of the wait.
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

    private static string? Locate()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        return new[] { "/bin/bash", "/usr/bin/bash", "/opt/homebrew/bin/bash" }.FirstOrDefault(File.Exists);
    }

    // Read straight from the process rather than through the channel: the version is reported in
    // test output, and putting it in the cassette would record an answer nothing ever replays.
    private static string DescribeVersion()
    {
        if (!IsLive)
        {
            return OracleRecordings.RecordedVersion(Tool) ?? "unavailable";
        }

        string version = Execute("echo $BASH_VERSION", System.IO.Path.GetTempPath()).Stdout.Trim();
        OracleRecordings.NoteVersion(Tool, version);

        return version;
    }
}
