using System.Text;

namespace Sharp.Shell;

// One spelling for one command. A host matches its rules against text, so `dotnet build "my proj"`
// and `dotnet build my\ proj` must not reach it as two different targets — they are the same argv
// and they get the same text.
//
// The text is post-expansion, which is the whole point and also the caveat: `dotnet $VERB` is
// spelled `dotnet build`. That is what actually runs, and it is not what the user typed.
public static class CommandText
{
    public static string Of(string program, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(arguments);

        StringBuilder text = new(Quote(program));

        foreach (string argument in arguments)
        {
            text.Append(' ').Append(Quote(argument));
        }

        return text.ToString();
    }

    // POSIX single quoting: everything between the quotes is literal, and the one character that
    // cannot appear inside is the quote itself — which closes the run, escapes a quote, and reopens.
    public static string Quote(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        return NeedsQuoting(word)
            ? $"'{word.Replace("'", "'\\''", StringComparison.Ordinal)}'"
            : word;
    }

    private static bool NeedsQuoting(string word) => word.Length == 0 || !word.All(IsBare);

    // Bare is the conservative set: letters and digits, and the punctuation that means nothing to a
    // shell in an argument. Everything else — whitespace, quotes, `$`, backticks, glob characters,
    // `~`, `!`, `#` — is quoted rather than reasoned about.
    private static bool IsBare(char character) =>
        char.IsLetterOrDigit(character) || "_-./:@+,%=".Contains(character, StringComparison.Ordinal);
}
