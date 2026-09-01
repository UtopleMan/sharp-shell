using System.Text;

namespace Sharp.Shell.Commands.Sed;

internal sealed record SedRecord(string Text, bool HasTrailingSeparator, bool IsLast);

// Turns the chunk stream into sed's records, one at a time and one record ahead. The lookahead is
// what makes `$` answerable at all: the last-line address has to be known while the last line is
// still being processed, and materialising the input to find out would break the laziness that lets
// `sed -n 1p huge | head` stop reading.
internal sealed class SedRecordReader(IEnumerable<string> input, char separator)
{
    private readonly IEnumerator<(string Text, bool HasSeparator)> source = Split(input, separator).GetEnumerator();

    private (string Text, bool HasSeparator)? pending;

    private bool hasStarted;

    public bool TryRead(out SedRecord record)
    {
        if (!hasStarted)
        {
            hasStarted = true;
            pending = source.MoveNext() ? source.Current : null;
        }

        if (pending is null)
        {
            record = new SedRecord(string.Empty, false, true);
            return false;
        }

        (string text, bool hasSeparator) = pending.Value;
        pending = source.MoveNext() ? source.Current : null;
        record = new SedRecord(text, hasSeparator, pending is null);
        return true;
    }

    private static IEnumerable<(string Text, bool HasSeparator)> Split(IEnumerable<string> chunks, char separator)
    {
        StringBuilder record = new();

        foreach (string chunk in chunks)
        {
            int start = 0;

            while (true)
            {
                int position = chunk.IndexOf(separator, start);

                if (position < 0)
                {
                    record.Append(chunk, start, chunk.Length - start);
                    break;
                }

                record.Append(chunk, start, position - start);
                yield return (record.ToString(), true);
                record.Clear();
                start = position + 1;
            }
        }

        if (record.Length > 0)
        {
            yield return (record.ToString(), false);
        }
    }
}
