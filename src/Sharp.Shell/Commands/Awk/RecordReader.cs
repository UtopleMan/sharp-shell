using System.Text;

namespace Sharp.Shell.Commands.Awk;

// Splits the input stream into records. RS is read before every record rather than once, because an
// assignment to it takes effect for the record after the one being read.
//
// A multi-character RS uses its first character, which is what POSIX says and what one-true-awk
// does; regular-expression record separators are a gawk extension this dialect does not implement.
// `RS = ""` is paragraph mode: leading blank lines are skipped, a record ends at the first blank
// line, and the blank-line run between records is consumed whole.
internal sealed class RecordReader(IEnumerable<string> chunks) : IDisposable
{
    private readonly IEnumerator<string> source = chunks.GetEnumerator();

    private readonly StringBuilder carried = new();

    private string chunk = string.Empty;

    private int offset;

    private bool isExhausted;

    public bool TryRead(string recordSeparator, out string record) => recordSeparator.Length == 0
        ? TryReadParagraph(out record)
        : TryReadDelimited(recordSeparator[0], out record);

    private bool TryReadDelimited(char separator, out string record)
    {
        while (true)
        {
            if (offset >= chunk.Length && !TryAdvanceChunk())
            {
                return TryTakeCarried(out record);
            }

            int found = chunk.IndexOf(separator, offset);

            if (found >= 0)
            {
                carried.Append(chunk, offset, found - offset);
                offset = found + 1;
                record = carried.ToString();
                carried.Clear();
                return true;
            }

            carried.Append(chunk, offset, chunk.Length - offset);
            offset = chunk.Length;
        }
    }

    // Built on the line reader so a record spanning chunks needs no lookahead: skip the blank lines
    // in front, then take lines until one is blank or the input ends.
    private bool TryReadParagraph(out string record)
    {
        string line;

        do
        {
            if (!TryReadDelimited('\n', out line))
            {
                record = string.Empty;
                return false;
            }
        }
        while (line.Length == 0);

        StringBuilder paragraph = new(line);

        while (TryReadDelimited('\n', out line) && line.Length > 0)
        {
            paragraph.Append('\n').Append(line);
        }

        record = paragraph.ToString();
        return true;
    }

    private bool TryTakeCarried(out string record)
    {
        record = carried.ToString();
        carried.Clear();

        return record.Length > 0;
    }

    private bool TryAdvanceChunk()
    {
        while (!isExhausted)
        {
            if (!source.MoveNext())
            {
                isExhausted = true;
                return false;
            }

            if (source.Current.Length == 0)
            {
                continue;
            }

            chunk = source.Current;
            offset = 0;
            return true;
        }

        return false;
    }

    public void Dispose() => source.Dispose();
}
