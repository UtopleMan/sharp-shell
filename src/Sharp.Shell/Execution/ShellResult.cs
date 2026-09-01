namespace Sharp.Shell.Execution;

public sealed record ShellResult(int ExitCode, string Stdout, string Stderr);
