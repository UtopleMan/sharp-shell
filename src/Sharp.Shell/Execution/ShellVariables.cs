using System.Collections;

namespace Sharp.Shell.Execution;

// The variable table: a value per name, plus whether a child process should see it. One table with
// a flag rather than two dictionaries, because the two-dictionary model loses the flag the moment an
// exported name is reassigned — `export PATH=x; PATH=y` must stay exported.
//
// It reads as IReadOnlyDictionary<string, string> so every caller that only wants values — parameter
// expansion, arithmetic, awk's ENVIRON — keeps working unchanged.
public sealed class ShellVariables : IReadOnlyDictionary<string, string>
{
    private static readonly Dictionary<string, string> EMPTY_ENVIRONMENT = [];

    private readonly Dictionary<string, Entry> byName = new(StringComparer.Ordinal);

    // The environment a host hands the shell. Every entry arrives exported, because that is what an
    // inherited variable is; the core reads nothing from the operating system itself, which is what
    // keeps it compiling into the wasm guest.
    public static ShellVariables SeededWith(IReadOnlyDictionary<string, string>? environment)
    {
        ShellVariables variables = new();

        foreach (KeyValuePair<string, string> entry in environment ?? EMPTY_ENVIRONMENT)
        {
            variables.byName[entry.Key] = new Entry(entry.Value, IsExported: true);
        }

        return variables;
    }

    public string this[string name]
    {
        get => byName[name].Value;
        set => byName[name] = new Entry(value, IsExported(name));
    }

    public int Count => byName.Count;

    public IEnumerable<string> Keys => byName.Keys;

    public IEnumerable<string> Values => byName.Values.Select(entry => entry.Value);

    public bool ContainsKey(string name) => byName.ContainsKey(name);

    public bool TryGetValue(string name, out string value)
    {
        if (byName.TryGetValue(name, out Entry entry))
        {
            value = entry.Value;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public bool Remove(string name) => byName.Remove(name);

    public bool IsExported(string name) => byName.TryGetValue(name, out Entry entry) && entry.IsExported;

    // Exporting a name the shell has never seen creates it empty rather than dropping the mark, so a
    // later `FOO=bar` reaches the child the way `export FOO; FOO=bar` does in bash.
    public void Export(string name) => byName[name] = new Entry(ValueOrEmpty(name), true);

    public void Unexport(string name)
    {
        if (byName.TryGetValue(name, out Entry entry))
        {
            byName[name] = entry with { IsExported = false };
        }
    }

    // The subset a child process receives.
    public IReadOnlyDictionary<string, string> Exported =>
        byName
            .Where(variable => variable.Value.IsExported)
            .ToDictionary(variable => variable.Key, variable => variable.Value.Value, StringComparer.Ordinal);

    // Values and marks together, which is what a fork needs: exported-ness survives a fork exactly
    // as the values do.
    public void CopyTo(ShellVariables destination)
    {
        foreach (KeyValuePair<string, Entry> variable in byName)
        {
            destination.byName[variable.Key] = variable.Value;
        }
    }

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
        byName.Select(variable => KeyValuePair.Create(variable.Key, variable.Value.Value)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private string ValueOrEmpty(string name) => byName.TryGetValue(name, out Entry entry) ? entry.Value : string.Empty;

    private readonly record struct Entry(string Value, bool IsExported);
}
