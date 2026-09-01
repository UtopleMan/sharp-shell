namespace Sharp.Shell.Commands.Sed;

// Every path a sed script names resolves through here, so `w ../escape.txt` fails the same way
// `rm ../thing` does. The wasm preopen bounds this too, but the library also runs natively, and a
// script that writes outside the workspace is the one mistake that cannot be undone by retrying.
internal sealed class SedFileSystem(AppletContext context) : ISedFileSystem, IDisposable
{
    private const int IoErrorExitCode = 4;

    private readonly Dictionary<string, StreamWriter> writers = new(StringComparer.Ordinal);

    private readonly Dictionary<string, IEnumerator<string>> lineReaders = new(StringComparer.Ordinal);

    private readonly HashSet<string> refusedPaths = new(StringComparer.Ordinal);

    public int ErrorExitCode { get; private set; }

    public string ReadAll(string path)
    {
        string? absolute = ResolveForReading(path);

        return absolute is null || !File.Exists(absolute) ? string.Empty : File.ReadAllText(absolute);
    }

    public string? ReadLine(string path)
    {
        if (!lineReaders.TryGetValue(path, out IEnumerator<string>? reader))
        {
            string? absolute = ResolveForReading(path);
            reader = absolute is not null && File.Exists(absolute)
                ? File.ReadLines(absolute).GetEnumerator()
                : Enumerable.Empty<string>().GetEnumerator();
            lineReaders[path] = reader;
        }

        return reader.MoveNext() ? reader.Current : null;
    }

    // Every `w` target exists from the moment the script loads, empty, whether or not a line ever
    // matches — so a stale file from a previous run cannot be mistaken for this run's output.
    public void Create(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (IsStandardOutput(path) || IsStandardError(path))
            {
                continue;
            }

            OpenWriter(path);
        }
    }

    // sed truncates a `w` target once and appends for the rest of the run, which is why the writer
    // is opened on first use and kept.
    public void Append(string path, string text)
    {
        if (refusedPaths.Contains(path))
        {
            return;
        }

        StreamWriter? writer = OpenWriter(path);
        writer?.Write(text);
    }

    private StreamWriter? OpenWriter(string path)
    {
        if (refusedPaths.Contains(path))
        {
            return null;
        }

        if (writers.TryGetValue(path, out StreamWriter? existing))
        {
            return existing;
        }

        if (!MutationGuard.TryResolve(path, context, "sed", out string absolute))
        {
            refusedPaths.Add(path);
            ErrorExitCode = IoErrorExitCode;
            return null;
        }

        StreamWriter writer = new(absolute, append: false);
        writers[path] = writer;
        return writer;
    }

    public bool IsStandardOutput(string path) => path is "/dev/stdout";

    public bool IsStandardError(string path) => path is "/dev/stderr";

    private string? ResolveForReading(string path)
    {
        string absolute = context.State.Resolve(path);

        if (context.State.IsInsideRoot(absolute))
        {
            return absolute;
        }

        context.WriteError($"sed: {path}: outside the workspace\n");
        ErrorExitCode = IoErrorExitCode;
        return null;
    }

    public void Dispose()
    {
        foreach (StreamWriter writer in writers.Values)
        {
            writer.Dispose();
        }

        foreach (IEnumerator<string> reader in lineReaders.Values)
        {
            reader.Dispose();
        }

        writers.Clear();
        lineReaders.Clear();
    }
}
