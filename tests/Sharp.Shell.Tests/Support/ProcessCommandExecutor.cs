using System.Diagnostics;
using System.Runtime.InteropServices;
using Sharp.Shell;
using Sharp.Shell.Execution;

namespace Sharp.Shell.Tests.Support;

// The native side of the ICommandExecutor seam, for tests only. The library itself must never
// reference System.Diagnostics: it compiles into a wasm guest that has no processes at all.
//
// This is what unlocks the half of the conformance corpus that calls external helpers, and it is
// where those helpers are reimplemented in C# so the suite stays hermetic and python-free.
internal sealed class ProcessCommandExecutor : ICommandExecutor
{
    public static bool IsAvailable => !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || File.Exists(ShellPath());

    public CommandExecution Execute(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IEnumerable<string> input,
        CancellationToken cancellationToken)
    {
        if (SpecHelpers.TryRun(program, arguments, out CommandExecution helper))
        {
            return helper;
        }

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = program,
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

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return CommandExecution.NotSupported;
        }

        process.StandardInput.Write(TextStream.Collect(input));
        process.StandardInput.Close();

        // Bounded like the oracles: a command that never exits must fail the one test that ran it,
        // not hang the suite.
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        bool exited = process.WaitForExit(milliseconds: 10_000);

        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        return new CommandExecution(true, exited ? process.ExitCode : -1, TextStream.FromText(output.Result), error.Result);
    }

    // The shell the native tier would use. bash rather than sh, because the language this project
    // emulates is bash, and cmd.exe on Windows.
    public static string ShellPath() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Environment.GetEnvironmentVariable("COMSPEC") ?? @"C:\Windows\System32\cmd.exe"
            : File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
}

// The oils spec corpus calls four tiny python2 helpers. Vendoring them would put a python
// dependency on this suite, so they are reimplemented here instead — they are trivial.
public static class SpecHelpers
{
    public static bool TryRun(string program, IReadOnlyList<string> arguments, out CommandExecution execution)
    {
        execution = CommandExecution.NotSupported;

        switch (program)
        {
            case "argv.py":
                execution = Ok($"[{string.Join(", ", arguments.Select(argument => $"'{argument}'"))}]\n");
                return true;
            case "printenv.py":
                execution = Ok(string.Concat(arguments.Select(name =>
                    $"{Environment.GetEnvironmentVariable(name) ?? "None"}\n")));
                return true;
            default:
                return false;
        }
    }

    private static CommandExecution Ok(string output) => new(true, 0, TextStream.FromText(output), string.Empty);
}
