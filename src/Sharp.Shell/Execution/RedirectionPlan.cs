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

    public string? Failure { get; init; }

    public static RedirectionPlan Failed(string failure) => new() { Failure = failure };
}
