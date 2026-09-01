namespace Sharp.Shell.Commands.Awk;

// Every name a program can reach: the globals, the special variables, and the innermost function
// frame. A special variable is a normal slot with a default, so an assignment to it takes effect the
// moment it happens — the only one that cannot be is NF, which is a question about the record rather
// than a value of its own.
internal sealed class AwkEnvironment
{
    private const string FieldCountName = "NF";

    private readonly Dictionary<string, AwkSlot> globals = new(StringComparer.Ordinal);

    private readonly List<Dictionary<string, AwkSlot>> frames = [];

    public AwkEnvironment(AwkRegexCache regexes)
    {
        Record = new AwkRecord(regexes);

        Write("FS", AwkValue.Of(" "));
        Write("OFS", AwkValue.Of(" "));
        Write("ORS", AwkValue.Of("\n"));
        Write("RS", AwkValue.Of("\n"));
        Write("SUBSEP", AwkValue.Of("\x1c"));
        Write("CONVFMT", AwkValue.Of(AwkPrintf.DefaultNumberFormat));
        Write("OFMT", AwkValue.Of(AwkPrintf.DefaultNumberFormat));
        Write("FILENAME", AwkValue.Of(string.Empty));
        Write("NR", AwkValue.Of(0d));
        Write("FNR", AwkValue.Of(0d));
        Write("RSTART", AwkValue.Of(0d));
        Write("RLENGTH", AwkValue.Of(-1d));
    }

    public AwkRecord Record { get; }

    public string FieldSeparator => Text("FS");

    public string OutputFieldSeparator => Text("OFS");

    public string OutputRecordSeparator => Text("ORS");

    public string RecordSeparator => Text("RS");

    public string SubscriptSeparator => Text("SUBSEP");

    public string ConvertFormat => Text("CONVFMT");

    public string OutputFormat => Text("OFMT");

    public bool IsParagraphMode => RecordSeparator.Length == 0;

    public AwkValue Read(string name) =>
        name == FieldCountName ? AwkValue.Of(Record.FieldCount) : Slot(name).Value;

    public void Write(string name, AwkValue value)
    {
        if (name == FieldCountName)
        {
            Record.SetFieldCount((int)value.ToNumber(), OutputFieldSeparator);
            return;
        }

        Slot(name).Value = value;
    }

    public AwkArray Array(string name) => Slot(name).Array;

    public AwkSlot Slot(string name)
    {
        if (frames.Count > 0 && frames[^1].TryGetValue(name, out AwkSlot? local))
        {
            return local;
        }

        if (!globals.TryGetValue(name, out AwkSlot? global))
        {
            global = new AwkSlot();
            globals[name] = global;
        }

        return global;
    }

    public bool IsLocal(string name) => frames.Count > 0 && frames[^1].ContainsKey(name);

    public void PushFrame(Dictionary<string, AwkSlot> frame) => frames.Add(frame);

    public void PopFrame() => frames.RemoveAt(frames.Count - 1);

    public int Depth => frames.Count;

    // The special variables are read with the default number format rather than CONVFMT, so that
    // reading CONVFMT itself cannot depend on CONVFMT.
    private string Text(string name) => Read(name).ToStringWith(AwkPrintf.DefaultNumberFormat);
}
