using System.Text;

namespace Sharp.Shell.Commands;

// The backslash escapes echo -e and printf share, and the one place they part company: printf reads
// a bare `\101` as an octal byte, echo -e prints it literally and wants the leading zero (`\0101`).
// An escape neither of them defines keeps its backslash, the way bash prints `\q` for `\q` — dropping
// it silently turns a typo into different text instead of showing it.
internal static class Escapes
{
    public static string InterpretPrintfFormat(string format) => Interpret(format, readsBareOctal: true);

    public static string InterpretEchoText(string text) => Interpret(text, readsBareOctal: false);

    private static string Interpret(string text, bool readsBareOctal)
    {
        StringBuilder output = new(text.Length);

        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != '\\' || index + 1 >= text.Length)
            {
                output.Append(text[index]);
                continue;
            }

            index++;
            AppendEscape(output, text, ref index, readsBareOctal);
        }

        return output.ToString();
    }

    private static void AppendEscape(StringBuilder output, string text, ref int index, bool readsBareOctal)
    {
        char escaped = text[index];

        if (SingleCharacterEscape(escaped) is { } single)
        {
            output.Append(single);
            return;
        }

        if (TryAppendOctal(output, text, ref index, readsBareOctal))
        {
            return;
        }

        if (escaped == 'x' && index + 1 < text.Length && IsDigit(text[index + 1], radix: 16))
        {
            index++;
            output.Append(ReadNumericEscape(text, ref index, radix: 16, maximumDigits: 2));
            return;
        }

        output.Append('\\').Append(escaped);
    }

    private static char? SingleCharacterEscape(char escaped) => escaped switch
    {
        'a' => '\a',
        'b' => '\b',
        'e' or 'E' => '\u001b',
        'f' => '\f',
        'n' => '\n',
        'r' => '\r',
        't' => '\t',
        'v' => '\v',
        '\\' => '\\',
        '"' => '"',
        '\'' => '\'',
        '?' => '?',
        _ => null,
    };

    // printf counts the leading zero as one of the three digits, so `\010` is a backspace and
    // `\0101` is that backspace followed by a 1. echo -e treats the zero as a prefix and reads three
    // digits after it, so the same `\0101` is an A — and it refuses a bare `\101` altogether.
    private static bool TryAppendOctal(StringBuilder output, string text, ref int index, bool readsBareOctal)
    {
        char escaped = text[index];

        if (readsBareOctal && IsDigit(escaped, radix: 8))
        {
            output.Append(ReadNumericEscape(text, ref index, radix: 8, maximumDigits: 3));
            return true;
        }

        if (!readsBareOctal && escaped == '0')
        {
            index++;
            output.Append(ReadNumericEscape(text, ref index, radix: 8, maximumDigits: 3));
            return true;
        }

        return false;
    }

    // Leaves the index on the last digit consumed, because the caller's loop advances past it.
    private static char ReadNumericEscape(string text, ref int index, int radix, int maximumDigits)
    {
        int value = 0;
        int digits = 0;

        while (digits < maximumDigits && index < text.Length && IsDigit(text[index], radix))
        {
            value = (value * radix) + DigitValue(text[index]);
            index++;
            digits++;
        }

        index--;
        return (char)value;
    }

    private static bool IsDigit(char character, int radix)
    {
        int digit = DigitValue(character);
        return digit >= 0 && digit < radix;
    }

    private static int DigitValue(char character) =>
        char.IsAsciiDigit(character) ? character - '0'
        : char.IsAsciiHexDigit(character) ? char.ToLowerInvariant(character) - 'a' + 10
        : -1;
}
