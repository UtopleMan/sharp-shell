using System.Globalization;

namespace Sharp.Shell.Commands.Awk;

// Where a string becomes a number, and where a string from input becomes a *numeric* string.
//
// The two questions are different. `Prefix` answers what `"12abc" + 0` is — 12, because awk converts
// as much of the front as looks like a number and stops. `IsNumeric` answers whether a field
// compares numerically, which needs the *whole* string, blanks aside, to be a number.
//
// Hexadecimal is recognised in both, which is not what a plain reading of POSIX suggests but is what
// both reference implementations do: `echo 0x1A | awk '{print $1 + 0}'` prints 26 under one-true-awk
// and under `gawk --posix`. `inf` and `nan` are deliberately not recognised — there the two oracles
// spell the answer differently and POSIX-strict is the tie-breaker.
internal static class AwkNumericText
{
    public static bool IsNumeric(string text)
    {
        int cursor = 0;
        SkipBlanks(text, ref cursor);

        if (!TryReadNumber(text, ref cursor, out _))
        {
            return false;
        }

        SkipBlanks(text, ref cursor);
        return cursor == text.Length;
    }

    public static double Prefix(string text)
    {
        int cursor = 0;
        SkipBlanks(text, ref cursor);

        return TryReadNumber(text, ref cursor, out double value) ? value : 0;
    }

    private static void SkipBlanks(string text, ref int cursor)
    {
        while (cursor < text.Length && text[cursor] is ' ' or '\t' or '\n' or '\r' or '\f' or '\v')
        {
            cursor++;
        }
    }

    private static bool TryReadNumber(string text, ref int cursor, out double value)
    {
        value = 0;
        int start = cursor;
        bool isNegative = false;

        if (cursor < text.Length && text[cursor] is '+' or '-')
        {
            isNegative = text[cursor] == '-';
            cursor++;
        }

        if (TryReadHexadecimal(text, ref cursor, out double hexadecimal))
        {
            value = isNegative ? -hexadecimal : hexadecimal;
            return true;
        }

        if (!TryReadDecimal(text, ref cursor))
        {
            cursor = start;
            return false;
        }

        value = double.Parse(text[start..cursor], NumberStyles.Float, CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryReadHexadecimal(string text, ref int cursor, out double value)
    {
        value = 0;

        if (cursor + 2 >= text.Length
            || text[cursor] != '0'
            || text[cursor + 1] is not ('x' or 'X')
            || !char.IsAsciiHexDigit(text[cursor + 2]))
        {
            return false;
        }

        cursor += 2;

        while (cursor < text.Length && char.IsAsciiHexDigit(text[cursor]))
        {
            value = (value * 16) + HexValue(text[cursor]);
            cursor++;
        }

        return true;
    }

    private static int HexValue(char digit) =>
        char.IsAsciiDigit(digit) ? digit - '0' : char.ToLowerInvariant(digit) - 'a' + 10;

    private static bool TryReadDecimal(string text, ref int cursor)
    {
        int digits = ReadDigits(text, ref cursor);

        if (cursor < text.Length && text[cursor] == '.')
        {
            cursor++;
            digits += ReadDigits(text, ref cursor);
        }

        if (digits == 0)
        {
            return false;
        }

        ReadExponent(text, ref cursor);
        return true;
    }

    private static void ReadExponent(string text, ref int cursor)
    {
        if (cursor >= text.Length || text[cursor] is not ('e' or 'E'))
        {
            return;
        }

        int probe = cursor + 1;

        if (probe < text.Length && text[probe] is '+' or '-')
        {
            probe++;
        }

        if (ReadDigits(text, ref probe) == 0)
        {
            return;
        }

        cursor = probe;
    }

    private static int ReadDigits(string text, ref int cursor)
    {
        int start = cursor;

        while (cursor < text.Length && char.IsAsciiDigit(text[cursor]))
        {
            cursor++;
        }

        return cursor - start;
    }
}
