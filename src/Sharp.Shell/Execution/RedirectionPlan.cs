namespace Sharp.Shell.Execution;

internal enum StreamTarget
{
    Default,
    File,
    OtherStream,
}

// Where one command's three streams go once its redirections are resolved. Resolution happens
// before the applet runs, because stderr has to be redirected before anything can write to it.
internal sealed record RedirectionPlan
{
    public IEnumerable<string>? Input { get; init; }

    public StreamTarget Output { get; init; } = StreamTarget.Default;

    public string? OutputPath { get; init; }

    public bool OutputAppends { get; init; }

    public StreamTarget Error { get; init; } = StreamTarget.Default;

    public string? ErrorPath { get; init; }

    public bool ErrorAppends { get; init; }

    // Whether this command's output leaves the default streams. A redirection is resolved in the
    // caller, long after the approver has seen the command, so a host asked only about `echo hi`
    // would consent to `echo hi > out.txt` without ever being told about the write. Duplication
    // (`2>&1`) opens no file and is still counted: the flag is read as "do not treat this as a
    // plain read", and the cost of counting it is one prompt.
    public bool WritesOutput { get; init; }

    public string? Failure { get; init; }

    public static RedirectionPlan Failed(string failure) => new() { Failure = failure };
}
