using System.Text;

namespace Sharp.Shell.Commands.Awk;

// awk's escape table, in one place because three callers need exactly the same rules: string
// literals, regular expression literals, and the values of `-v` and command-line assignments.
//
// An escape awk does not define loses its backslash — `"a\qb"` is `aqb` — which is what both
// reference implementations do.
internal static class AwkEscapes
{
    public static string Decode(string text)
    {
        StringBuilder decoded = new(text.Length);

        for (int index = 0; index < text.Length;)
        {
            if (text[index] != '\\' || index + 1 >= text.Length)
            {
                decoded.Append(text[index]);
                index++;
                continue;
            }

            Append(decoded, text, ref index);
        }

        return decoded.ToString();
    }

    // Appends the escape starting at the backslash under `index` and advances past it.
    public static void Append(StringBuilder target, string source, ref int index)
    {
        char escaped = source[index + 1];

        if (escaped is >= '0' and <= '7')
        {
            AppendOctal(target, source, ref index);
            return;
        }

        target.Append(Character(escaped));
        index += 2;
    }

    public static char Character(char escaped) => escaped switch
    {
        'n' => '\n',
        't' => '\t',
        'r' => '\r',
        'b' => '\b',
        'f' => '\f',
        'v' => '\v',
        'a' => '\a',
        _ => escaped,
    };

    public static bool IsTableEscape(char escaped) =>
        escaped is 'n' or 't' or 'r' or 'b' or 'f' or 'v' or 'a' or '/' or '\\' or '"';

    private static void AppendOctal(StringBuilder target, string source, ref int index)
    {
        int cursor = index + 1;
        int value = 0;
        int digits = 0;

        while (cursor < source.Length && digits < 3 && source[cursor] is >= '0' and <= '7')
        {
            value = (value * 8) + (source[cursor] - '0');
            cursor++;
            digits++;
        }

        target.Append((char)value);
        index = cursor;
    }
}
