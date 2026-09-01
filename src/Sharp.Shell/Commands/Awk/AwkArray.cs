namespace Sharp.Shell.Commands.Awk;

// An awk array is a map from a string subscript to a value, and reading an element creates it —
// which is why `if (a[k])` and `if (k in a)` answer differently on the second call.
internal sealed class AwkArray
{
    private readonly Dictionary<string, AwkValue> entries = new(StringComparer.Ordinal);

    public int Count => entries.Count;

    // A snapshot, because `for (k in a) delete a[k]` is a normal thing to write.
    public IReadOnlyList<string> Subscripts => [.. entries.Keys];

    public bool Contains(string subscript) => entries.ContainsKey(subscript);

    public AwkValue Read(string subscript)
    {
        if (entries.TryGetValue(subscript, out AwkValue existing))
        {
            return existing;
        }

        entries[subscript] = AwkValue.Uninitialized;
        return AwkValue.Uninitialized;
    }

    public void Write(string subscript, AwkValue value) => entries[subscript] = value;

    public void Remove(string subscript) => entries.Remove(subscript);

    public void Clear() => entries.Clear();
}
