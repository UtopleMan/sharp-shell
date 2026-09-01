namespace Sharp.Shell.Execution;

// The outcome of one bash invocation. Result is null exactly when the classification says Native:
// nothing ran, nothing was touched, and the caller escalates the original command string.
public sealed record ShellRun(Classification Classification, ShellResult? Result);
