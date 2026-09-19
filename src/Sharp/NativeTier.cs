using System.Diagnostics;
using System.Runtime.InteropServices;
using Sharp.Shell;
using Sharp.Shell.Execution;

namespace Sharp;

// The binary's process seam. Every process this shell starts is started here, and only because
// ShellExecutor asked — for one command it does not own, or for a whole line it cannot run. The
// binary has no path of its own to a real shell any more.
internal sealed class NativeTier : ICommandExecutor
{
    public CommandExecution Execute(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IEnumerable<string> input,
        CancellationToken cancellationToken) =>
        RunCapturing(program, arguments, workingDirectory, input, cancellationToken);

    // A line handed over whole keeps the terminal: it is the escape hatch for constructs this shell
    // has no model for, and capturing its output would break the interactive programs that are the
    // reason anyone reaches for it.
    public CommandExecution ExecuteLine(
        string commandLine,
        string workingDirectory,
        CancellationToken cancellationToken) =>
        RunAttached(Executable, [CommandFlag, commandLine], workingDirectory);

    private static CommandExecution RunCapturing(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IEnumerable<string> input,
        CancellationToken cancellationToken)
    {
        using Process process = Configure(program, arguments, workingDirectory, redirects: true);

        if (!TryStart(process))
        {
            return CommandExecution.NotSupported;
        }

        process.StandardInput.Write(TextStream.Collect(input));
        process.StandardInput.Close();

        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        process.WaitForExit();

        return new CommandExecution(true, process.ExitCode, TextStream.FromText(output.Result), error.Result);
    }

    private static CommandExecution RunAttached(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        using Process process = Configure(program, arguments, workingDirectory, redirects: false);

        if (!TryStart(process))
        {
            return CommandExecution.NotSupported;
        }

        process.WaitForExit();

        return new CommandExecution(true, process.ExitCode, TextStream.Empty, string.Empty);
    }

    private static Process Configure(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        bool redirects)
    {
        Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = program,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = redirects,
                RedirectStandardOutput = redirects,
                RedirectStandardError = redirects,
            },
        };

        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        return process;
    }

    private static bool TryStart(Process process)
    {
        try
        {
            process.Start();
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    // bash rather than sh where both exist: this shell emulates the bash language, so a line it
    // hands over should keep the semantics it would have had.
    private static string Executable =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe"
            : File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";

    private static string CommandFlag =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "/c" : "-c";
}
