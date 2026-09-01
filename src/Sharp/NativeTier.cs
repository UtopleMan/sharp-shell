using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sharp;

// Rule 2's other half. A line the shell does not fully own is never partly executed here — it is
// handed to the platform shell as the original string, so its semantics are whatever a real shell
// would have given it.
internal static class NativeTier
{
    public static int Run(string command, string workingDirectory)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
            },
        };

        process.StartInfo.ArgumentList.Add(CommandFlag);
        process.StartInfo.ArgumentList.Add(command);

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception failure)
        {
            Console.Error.WriteLine($"sharp: cannot start {Executable}: {failure.Message}");
            return 127;
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    // bash rather than sh where both exist: this shell emulates the bash language, so a line that
    // falls through should keep the semantics it would have had.
    private static string Executable =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe"
            : File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";

    private static string CommandFlag =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "/c" : "-c";
}
