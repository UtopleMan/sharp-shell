using System.Text;

namespace Sharp.Shell.Execution;

// The pipe medium: a lazily produced sequence of text chunks whose concatenation is the stream.
// Chunks rather than lines so byte-exact output survives a pipeline — whether a stream ended with
// a newline is observable, and the conformance corpus asserts on it. Laziness is the whole design:
// a consumer that stops enumerating stops its producer, which is this shell's SIGPIPE.
public static class TextStream
{
    public static IEnumerable<string> Empty { get; } = [];

    public static IEnumerable<string> FromText(string text) => text.Length == 0 ? Empty : [text];

    public static IEnumerable<string> Lines(IEnumerable<string> chunks)
    {
        StringBuilder pending = new();

        foreach (string chunk in chunks)
        {
            int start = 0;
            while (true)
            {
                int newline = chunk.IndexOf('\n', start);
                if (newline < 0)
                {
                    pending.Append(chunk, start, chunk.Length - start);
                    break;
                }

                pending.Append(chunk, start, newline - start);
                yield return pending.ToString();
                pending.Clear();
                start = newline + 1;
            }
        }

        if (pending.Length > 0)
        {
            yield return pending.ToString();
        }
    }

    public static IEnumerable<string> FromLines(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            yield return line;
            yield return "\n";
        }
    }

    public static string Collect(IEnumerable<string> chunks)
    {
        StringBuilder builder = new();
        foreach (string chunk in chunks)
        {
            builder.Append(chunk);
        }

        return builder.ToString();
    }
}
