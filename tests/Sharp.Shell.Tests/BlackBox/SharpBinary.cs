using System.Diagnostics;

namespace Sharp.Shell.Tests.BlackBox;

public sealed record SharpResult(string Stdout, string Stderr, int ExitCode);

// Locates and drives the built shsh executable. Everything here goes through the real process
// boundary — argv, stdio encoding, exit status — which is the whole point: the library tests prove
// the shell, and these prove the binary exposes it faithfully.
public static class SharpBinary
{
    public static string? Path { get; } = Locate();

    public static bool IsAvailable => Path is not null;

    // --norc on every one of these: the machine running the suite may have a ~/.shshenv, and a test
    // that reads it is measuring the developer's home directory. RcFileTests opts back in with a HOME
    // of its own.
    public static SharpResult RunCommand(string command, string root, params string[] options) =>
        Run([.. options, "--norc", "--root", root, "-c", command], root, standardInput: null);

    public static SharpResult RunScript(string scriptPath, string root, params string[] options) =>
        Run([.. options, "--norc", "--root", root, scriptPath], root, standardInput: null);

    public static SharpResult RunPiped(string script, string root, params string[] options) =>
        Run([.. options, "--norc", "--root", root], root, script);

    public static SharpResult Run(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string? standardInput,
        IReadOnlyDictionary<string, string>? environment = null)
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

        foreach (KeyValuePair<string, string> variable in environment ?? new Dictionary<string, string>())
        {
            process.StartInfo.Environment[variable.Key] = variable.Value;
        }

        process.Start();

        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
        }

        process.StandardInput.Close();

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(milliseconds: 20_000);

        return new SharpResult(stdout, stderr, process.HasExited ? process.ExitCode : -1);
    }

    private static string? Locate()
    {
        string executable = OperatingSystem.IsWindows() ? "shsh.exe" : "shsh";

        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            foreach (string configuration in new[] { "Debug", "Release" })
            {
                string candidate = System.IO.Path.Combine(
                    directory.FullName, "src", "Sharp", "bin", configuration, "net10.0", executable);

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
