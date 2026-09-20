namespace Sharp;

// Ctrl+C. A shell must not die of it: the running command stops and the prompt comes back.
//
// A fresh token per command is the whole point. One source reused for the session stays cancelled
// after the first Ctrl+C, and every command after it would run against a dead token — the shell
// would look alive and do nothing.
internal sealed class Interrupts : IDisposable
{
    private readonly Lock guard = new();

    private CancellationTokenSource source = new();

    // Whether Ctrl+C arrived since the last Reset. Set while the shell may be blocked reading a
    // line, which is why it is answered separately from the token.
    public bool WasRaised { get; private set; }

    public CancellationToken Token
    {
        get
        {
            lock (guard)
            {
                return source.Token;
            }
        }
    }

    public void Raise()
    {
        lock (guard)
        {
            WasRaised = true;
            source.Cancel();
        }
    }

    public void Reset()
    {
        lock (guard)
        {
            WasRaised = false;

            if (!source.IsCancellationRequested)
            {
                return;
            }

            source.Dispose();
            source = new CancellationTokenSource();
        }
    }

    public void Dispose() => source.Dispose();
}
