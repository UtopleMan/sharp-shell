namespace Sharp.Shell.Text;

// The C locale, spelled out. The shell library runs with InvariantGlobalization, so a class that
// silently widened with the ambient culture would make the same script answer differently on two
// machines.
internal static class PosixCharacterClasses
{
    private static readonly Dictionary<string, string> ByName = new(StringComparer.Ordinal)
    {
        ["alpha"] = "a-zA-Z",
        ["digit"] = "0-9",
        ["alnum"] = "a-zA-Z0-9",
        ["upper"] = "A-Z",
        ["lower"] = "a-z",
        ["space"] = @" \t\n\v\f\r",
        ["blank"] = @" \t",
        ["punct"] = @"!-/:-@\[-`{-~",
        ["print"] = @"\x20-\x7e",
        ["graph"] = @"\x21-\x7e",
        ["cntrl"] = @"\x00-\x1f\x7f",
        ["xdigit"] = "0-9A-Fa-f",
        ["word"] = "a-zA-Z0-9_",
    };

    public const string Word = "a-zA-Z0-9_";

    public const string Space = @" \t\n\v\f\r";

    public static string? Translate(string name) => ByName.GetValueOrDefault(name);
}
