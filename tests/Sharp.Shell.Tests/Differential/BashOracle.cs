using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sharp.Shell.Tests.Differential;

public sealed record OracleResult(string Stdout, string Stderr, int ExitCode);

// Runs a command through the system bash so its answer can be compared with ours. The whole suite
// skips where bash is absent, which is every Windows agent.
public static class BashOracle
{
    public static string? Path { get; } = Locate();

    public static bool IsAvailable => Path is not null;

    public static string Version { get; } = IsAvailable ? ReadVersion() : "unavailable";

    public static OracleResult Run(string command, string workingDirectory)
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

        process.StartInfo.ArgumentList.Add("--norc");
        process.StartInfo.ArgumentList.Add("--noprofile");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(command);
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

    private static string ReadVersion()
    {
        OracleResult result = Run("echo $BASH_VERSION", System.IO.Path.GetTempPath());
        return result.Stdout.Trim();
    }
}
