using System.Globalization;
using System.Text;

namespace Sharp.Shell.Commands.Awk;

// C's printf, as awk uses it: for `printf` and `sprintf`, and for `CONVFMT`/`OFMT`, which are format
// strings applied to one number rather than settings with their own syntax.
//
// Running out of arguments is a failure, not a blank. POSIX says a missing one should be an empty
// string or a zero, but one-true-awk and `gawk --posix` both stop with an error instead — and a
// format that quietly prints a trailing space where the author expected a value is exactly the kind
// of silent wrong answer this tier exists to avoid.
internal static class AwkPrintf
{
    public const string DefaultNumberFormat = "%.6g";

    public static string Format(string format, IReadOnlyList<AwkValue> arguments, string convertFormat)
    {
        StringBuilder output = new();
        int argument = 0;
        int cursor = 0;

        while (cursor < format.Length)
        {
            if (format[cursor] != '%')
            {
                output.Append(format[cursor]);
                cursor++;
                continue;
            }

            cursor++;

            if (cursor < format.Length && format[cursor] == '%')
            {
                output.Append('%');
                cursor++;
                continue;
            }

            output.Append(NextConversion(format, ref cursor, arguments, ref argument, convertFormat));
        }

        return output.ToString();
    }

    // The entry point for CONVFMT and OFMT. A `%s` inside one of those converts the number with the
    // default format rather than re-entering the format being applied, which is what stops
    // `CONVFMT = "%s"` from recurring forever.
    public static string Number(double value, string format) =>
        Format(format, [AwkValue.Of(value)], DefaultNumberFormat);

    private static string NextConversion(
        string format,
        ref int cursor,
        IReadOnlyList<AwkValue> arguments,
        ref int argument,
        string convertFormat)
    {
        string flags = ReadFlags(format, ref cursor);
        int width = ReadSize(format, ref cursor, arguments, ref argument, out bool hasWidth);
        int precision = ReadPrecision(format, ref cursor, arguments, ref argument);

        if (cursor >= format.Length)
        {
            return string.Empty;
        }

        char kind = format[cursor];
        cursor++;

        if (width < 0)
        {
            flags += '-';
            width = -width;
        }

        AwkConversion conversion = new(kind, flags, hasWidth ? width : 0, precision);
        AwkValue value = Take(arguments, ref argument);

        return kind == 's' || kind == 'c'
            ? Pad(Text(conversion, value, convertFormat), conversion)
            : Pad(Convert(conversion, value.ToNumber()), conversion);
    }

    private static string ReadFlags(string format, ref int cursor)
    {
        int start = cursor;

        while (cursor < format.Length && format[cursor] is '-' or '+' or ' ' or '#' or '0')
        {
            cursor++;
        }

        return format[start..cursor];
    }

    private static int ReadSize(
        string format,
        ref int cursor,
        IReadOnlyList<AwkValue> arguments,
        ref int argument,
        out bool isPresent)
    {
        if (cursor < format.Length && format[cursor] == '*')
        {
            cursor++;
            isPresent = true;
            return TakeInteger(arguments, ref argument);
        }

        int start = cursor;

        while (cursor < format.Length && char.IsAsciiDigit(format[cursor]))
        {
            cursor++;
        }

        isPresent = cursor > start;
        return isPresent ? int.Parse(format[start..cursor], CultureInfo.InvariantCulture) : 0;
    }

    private static int ReadPrecision(
        string format,
        ref int cursor,
        IReadOnlyList<AwkValue> arguments,
        ref int argument)
    {
        if (cursor >= format.Length || format[cursor] != '.')
        {
            return -1;
        }

        cursor++;
        int precision = ReadSize(format, ref cursor, arguments, ref argument, out _);

        return Math.Max(precision, 0);
    }

    private static int TakeInteger(IReadOnlyList<AwkValue> arguments, ref int argument) =>
        (int)Take(arguments, ref argument).ToNumber();

    private static AwkValue Take(IReadOnlyList<AwkValue> arguments, ref int argument)
    {
        if (argument >= arguments.Count)
        {
            throw new AwkRuntimeException("not enough arguments to satisfy the format string");
        }

        return arguments[argument++];
    }

    // `%c` takes the code point when the argument is a number and the first character when it is a
    // string, which is why `printf "%c", 65` prints A and `printf "%c", "65"` prints 6.
    private static string Text(AwkConversion conversion, AwkValue value, string convertFormat)
    {
        if (conversion.Kind == 'c')
        {
            return Character(value, convertFormat);
        }

        string text = value.ToStringWith(convertFormat);

        return conversion.Precision >= 0 && conversion.Precision < text.Length
            ? text[..conversion.Precision]
            : text;
    }

    private static string Character(AwkValue value, string convertFormat)
    {
        if (value.Kind == AwkValueKind.Number)
        {
            int code = (int)value.ToNumber();
            return code is >= 0 and <= 0x10FFFF ? char.ConvertFromUtf32(code) : string.Empty;
        }

        string text = value.ToStringWith(convertFormat);
        return text.Length == 0 ? string.Empty : text[..1];
    }

    private static string Convert(AwkConversion conversion, double value) => conversion.Kind switch
    {
        'd' or 'i' => Signed(conversion, value),
        'o' or 'u' or 'x' or 'X' => Unsigned(conversion, value),
        'e' or 'E' => Exponential(conversion, value, conversion.Precision < 0 ? 6 : conversion.Precision),
        'f' or 'F' => Fixed(conversion, value, conversion.Precision < 0 ? 6 : conversion.Precision),
        'g' or 'G' => General(conversion, value),
        _ => string.Empty,
    };

    private static string Signed(AwkConversion conversion, double value)
    {
        long integer = Truncate(value);
        string digits = integer == long.MinValue
            ? "9223372036854775808"
            : Math.Abs(integer).ToString(CultureInfo.InvariantCulture);

        if (conversion.Precision >= 0)
        {
            digits = digits.PadLeft(conversion.Precision, '0');
        }

        return SignOf(conversion, integer < 0) + digits;
    }

    private static string Unsigned(AwkConversion conversion, double value)
    {
        ulong integer = unchecked((ulong)Truncate(value));

        string digits = conversion.Kind switch
        {
            'o' => System.Convert.ToString((long)integer, 8),
            'x' => integer.ToString("x", CultureInfo.InvariantCulture),
            'X' => integer.ToString("X", CultureInfo.InvariantCulture),
            _ => integer.ToString(CultureInfo.InvariantCulture),
        };

        if (conversion.Precision >= 0)
        {
            digits = digits.PadLeft(conversion.Precision, '0');
        }

        return conversion.IsAlternate ? Alternate(conversion, digits) : digits;
    }

    private static string Alternate(AwkConversion conversion, string digits) => conversion.Kind switch
    {
        'o' when !digits.StartsWith('0') => "0" + digits,
        'x' when digits != "0" => "0x" + digits,
        'X' when digits != "0" => "0X" + digits,
        _ => digits,
    };

    private static string Fixed(AwkConversion conversion, double value, int precision)
    {
        if (!double.IsFinite(value))
        {
            return NotFinite(conversion, value);
        }

        string digits = Math.Abs(value).ToString($"F{precision}", CultureInfo.InvariantCulture);

        if (precision == 0 && conversion.IsAlternate)
        {
            digits += ".";
        }

        return SignOf(conversion, IsNegative(value)) + digits;
    }

    private static string Exponential(AwkConversion conversion, double value, int precision)
    {
        if (!double.IsFinite(value))
        {
            return NotFinite(conversion, value);
        }

        string formatted = Math.Abs(value).ToString($"E{precision}", CultureInfo.InvariantCulture);
        (string mantissa, int exponent) = SplitExponential(formatted);

        if (precision == 0 && conversion.IsAlternate)
        {
            mantissa += ".";
        }

        char marker = char.IsUpper(conversion.Kind) ? 'E' : 'e';
        string sign = exponent < 0 ? "-" : "+";
        string magnitude = Math.Abs(exponent).ToString(CultureInfo.InvariantCulture).PadLeft(2, '0');

        return SignOf(conversion, IsNegative(value)) + mantissa + marker + sign + magnitude;
    }

    // C's %g: pick %e or %f from the exponent the value rounds to, then drop the trailing zeros the
    // choice left behind unless `#` asked for them.
    private static string General(AwkConversion conversion, double value)
    {
        if (!double.IsFinite(value))
        {
            return NotFinite(conversion, value);
        }

        int precision = conversion.Precision switch
        {
            < 0 => 6,
            0 => 1,
            int given => given,
        };

        int exponent = ExponentOf(value, precision - 1);

        string formatted = exponent < -4 || exponent >= precision
            ? Exponential(conversion, value, precision - 1)
            : Fixed(conversion, value, precision - 1 - exponent);

        return conversion.IsAlternate ? formatted : WithoutTrailingZeros(formatted);
    }

    private static int ExponentOf(double value, int precision)
    {
        if (value == 0)
        {
            return 0;
        }

        (_, int exponent) = SplitExponential(Math.Abs(value).ToString($"E{precision}", CultureInfo.InvariantCulture));
        return exponent;
    }

    private static (string Mantissa, int Exponent) SplitExponential(string formatted)
    {
        int marker = formatted.IndexOf('E', StringComparison.Ordinal);

        return (
            formatted[..marker],
            int.Parse(formatted[(marker + 1)..], NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
    }

    private static string WithoutTrailingZeros(string formatted)
    {
        int marker = formatted.IndexOfAny(['e', 'E']);
        string mantissa = marker < 0 ? formatted : formatted[..marker];
        string suffix = marker < 0 ? string.Empty : formatted[marker..];

        if (!mantissa.Contains('.', StringComparison.Ordinal))
        {
            return formatted;
        }

        mantissa = mantissa.TrimEnd('0').TrimEnd('.');
        return mantissa + suffix;
    }

    private static string NotFinite(AwkConversion conversion, double value)
    {
        string text = double.IsNaN(value) ? "nan" : "inf";
        text = char.IsUpper(conversion.Kind) ? text.ToUpperInvariant() : text;

        return SignOf(conversion, IsNegative(value)) + text;
    }

    private static string SignOf(AwkConversion conversion, bool isNegative) => isNegative
        ? "-"
        : conversion.ShowsSign ? "+"
        : conversion.SpacesSign ? " "
        : string.Empty;

    private static bool IsNegative(double value) => double.IsNegative(value) && value != 0;

    private static long Truncate(double value) => value switch
    {
        >= 9.2233720368547758E18 => long.MaxValue,
        <= -9.2233720368547758E18 => long.MinValue,
        _ => double.IsNaN(value) ? 0 : (long)value,
    };

    // A zero-padded number pads after its sign, and a precision on an integer conversion cancels the
    // padding altogether, which is why this cannot be a plain PadLeft.
    private static string Pad(string body, AwkConversion conversion)
    {
        if (body.Length >= conversion.Width)
        {
            return body;
        }

        if (conversion.LeftAligns)
        {
            return body.PadRight(conversion.Width);
        }

        if (!conversion.PadsWithZeros || !conversion.PadsNumbers)
        {
            return body.PadLeft(conversion.Width);
        }

        int signLength = body.Length > 0 && body[0] is '-' or '+' or ' ' ? 1 : 0;

        return body[..signLength] + body[signLength..].PadLeft(conversion.Width - signLength, '0');
    }
}

internal readonly record struct AwkConversion(char Kind, string Flags, int Width, int Precision)
{
    public bool LeftAligns => Flags.Contains('-', StringComparison.Ordinal);

    public bool ShowsSign => Flags.Contains('+', StringComparison.Ordinal);

    public bool SpacesSign => Flags.Contains(' ', StringComparison.Ordinal);

    public bool IsAlternate => Flags.Contains('#', StringComparison.Ordinal);

    public bool PadsWithZeros => Flags.Contains('0', StringComparison.Ordinal);

    public bool PadsNumbers => Kind is not ('s' or 'c')
        && (Precision < 0 || Kind is 'e' or 'E' or 'f' or 'F' or 'g' or 'G');
}
