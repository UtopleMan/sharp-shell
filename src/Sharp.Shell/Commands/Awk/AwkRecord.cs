namespace Sharp.Shell.Commands.Awk;

// `$0` and its fields, kept consistent in both directions: splitting `$0` produces the fields, and
// assigning a field rebuilds `$0` with OFS between them.
//
// The split is deferred but the separator is not. POSIX says a change to FS takes effect for the
// next record, so the value of FS is captured when the record arrives and used whenever the fields
// are first needed — which keeps `{ FS = ":"; print $1 }` reading the first record with the old
// separator, as awk does.
internal sealed class AwkRecord(AwkRegexCache regexes)
{
    private List<string> fields = [];

    private string separator = " ";

    private bool isParagraphMode;

    private bool isSplit = true;

    public string Text { get; private set; } = string.Empty;

    public void Set(string text, string fieldSeparator, bool isParagraph)
    {
        Text = text;
        separator = fieldSeparator;
        isParagraphMode = isParagraph;
        isSplit = false;
    }

    public int FieldCount
    {
        get
        {
            EnsureSplit();
            return fields.Count;
        }
    }

    public string Field(int index)
    {
        if (index == 0)
        {
            return Text;
        }

        EnsureSplit();
        return index <= fields.Count ? fields[index - 1] : string.Empty;
    }

    public void SetField(int index, string value, string outputSeparator)
    {
        if (index == 0)
        {
            Set(value, separator, isParagraphMode);
            return;
        }

        EnsureSplit();
        Extend(index);
        fields[index - 1] = value;
        Rebuild(outputSeparator);
    }

    // Assigning to NF truncates or extends the record, and either way `$0` is rebuilt from what is
    // left.
    public void SetFieldCount(int count, string outputSeparator)
    {
        EnsureSplit();
        int wanted = Math.Max(count, 0);

        if (wanted < fields.Count)
        {
            fields.RemoveRange(wanted, fields.Count - wanted);
        }
        else
        {
            Extend(wanted);
        }

        Rebuild(outputSeparator);
    }

    public void Rebuild(string outputSeparator) => Text = FieldSplitter.Join(fields, outputSeparator);

    private void Extend(int count)
    {
        while (fields.Count < count)
        {
            fields.Add(string.Empty);
        }
    }

    private void EnsureSplit()
    {
        if (isSplit)
        {
            return;
        }

        fields = [.. FieldSplitter.Split(Text, separator, isParagraphMode, regexes)];
        isSplit = true;
    }
}
