using System.Text;

namespace Sharp.Shell.Commands.Awk;

// Everywhere a program can write. Standard output is buffered and handed to the applet's iterator a
// record at a time, so `awk '{print}' big | head -1` still stops early; a redirection resolves
// through MutationGuard, so `print > "../escape"` fails the way `rm ../thing` does.
//
// A target is opened the first time something is written to it, not when the program loads. sed
// creates its `w` files up front, but awk does not: `awk '/never/ {print > "f"}'` leaves an existing
// `f` alone under one-true-awk and under `gawk --posix` alike. `>` truncates on that first open and
// appends afterwards, so closing and reopening truncates again.
internal sealed class AwkOutput(AppletContext context) : IDisposable
{
    private const int IoErrorExitCode = 2;

    private readonly StringBuilder standardOutput = new();

    private readonly Dictionary<string, StreamWriter> writers = new(StringComparer.Ordinal);

    private readonly HashSet<string> refusedPaths = new(StringComparer.Ordinal);

    public int ErrorExitCode { get; private set; }

    public void Write(string text) => standardOutput.Append(text);

    public string TakePending()
    {
        string pending = standardOutput.ToString();
        standardOutput.Clear();

        return pending;
    }

    public void WriteTo(string path, AwkRedirectionKind kind, string text)
    {
        if (path == "/dev/stdout")
        {
            Write(text);
            return;
        }

        if (path == "/dev/stderr")
        {
            context.WriteError(text);
            return;
        }

        OpenWriter(path, kind)?.Write(text);
    }

    // awk's close() answers 0 for a stream it had open and -1 for one it never did.
    public int Close(string path)
    {
        if (!writers.TryGetValue(path, out StreamWriter? writer))
        {
            return -1;
        }

        writer.Dispose();
        writers.Remove(path);

        return 0;
    }

    private StreamWriter? OpenWriter(string path, AwkRedirectionKind kind)
    {
        if (refusedPaths.Contains(path))
        {
            return null;
        }

        if (writers.TryGetValue(path, out StreamWriter? existing))
        {
            return existing;
        }

        if (!MutationGuard.TryResolve(path, context, "awk", out string absolute))
        {
            refusedPaths.Add(path);
            ErrorExitCode = IoErrorExitCode;
            return null;
        }

        StreamWriter writer = new(absolute, append: kind == AwkRedirectionKind.Append);
        writers[path] = writer;

        return writer;
    }

    public void Dispose()
    {
        foreach (StreamWriter writer in writers.Values)
        {
            writer.Dispose();
        }

        writers.Clear();
    }
}
