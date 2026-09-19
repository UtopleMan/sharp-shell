namespace Sharp.Shell.Execution;

// The outcome of one bash invocation. Result is always present: a line the shell cannot run itself
// is not handed back for the caller to deal with, it is asked for through ICommandExecutor.ExecuteLine
// and the answer comes back here like any other. Classification still describes the line, so a host
// can tell an owned line from one that needed a real program.
public sealed record ShellRun(Classification Classification, ShellResult Result);
