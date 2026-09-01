using System.Globalization;

namespace Sharp.Shell.Commands.Awk;

internal enum AwkValueKind
{
    Uninitialized,
    Number,
    String,

    // A string that arrived from outside the program and looks like a number. It carries both, and
    // compares as a number against a number and as a string against a string, which is the whole
    // reason `echo 10 9 | awk '{print ($1 > $2)}'` says 1 while `awk 'BEGIN{print ("10" > "9")}'`
    // says 0.
    StrNum,
}

internal readonly record struct AwkValue
{
    private readonly string? text;

    private readonly double number;

    private AwkValue(AwkValueKind kind, double number, string? text)
    {
        Kind = kind;
        this.number = number;
        this.text = text;
    }

    public AwkValueKind Kind { get; }

    public static AwkValue Uninitialized => default;

    public static AwkValue Of(double number) => new(AwkValueKind.Number, number, null);

    public static AwkValue Of(string text) => new(AwkValueKind.String, 0, text);

    public static AwkValue Of(bool truth) => Of(truth ? 1d : 0d);

    // Fields, `-v` and operand assignments, ARGV and ENVIRON entries, and split() results: everything
    // the program did not write as a literal.
    public static AwkValue FromInput(string text) => AwkNumericText.IsNumeric(text)
        ? new AwkValue(AwkValueKind.StrNum, AwkNumericText.Prefix(text), text)
        : new AwkValue(AwkValueKind.String, 0, text);

    // An uninitialized value compares equal to both 0 and "", so it counts as numeric here and its
    // string form is empty. An *empty field* is not the same thing — both reference implementations
    // make it a plain string, so `echo | awk '{print ($1 == 0)}'` prints 0.
    public bool ComparesNumerically => Kind is not AwkValueKind.String;

    public double ToNumber() => Kind switch
    {
        AwkValueKind.Number or AwkValueKind.StrNum => number,
        AwkValueKind.String => AwkNumericText.Prefix(text!),
        _ => 0,
    };

    public string ToStringWith(string convertFormat) => Kind switch
    {
        AwkValueKind.Number => NumberText(number, convertFormat),
        AwkValueKind.String or AwkValueKind.StrNum => text!,
        _ => string.Empty,
    };

    public bool IsTrue() => Kind switch
    {
        AwkValueKind.Number or AwkValueKind.StrNum => ToNumber() != 0,
        AwkValueKind.String => text!.Length > 0,
        _ => false,
    };

    // An integral value prints as an integer whatever CONVFMT or OFMT say, which is why `print 2^53`
    // is 9007199254740992 rather than 9.0072e+15.
    private static string NumberText(double value, string format) =>
        IsIntegral(value) ? IntegerText(value) : AwkPrintf.Number(value, format);

    private static bool IsIntegral(double value) => double.IsFinite(value) && value == Math.Truncate(value);

    private static string IntegerText(double value) =>
        value == 0 ? "0" : value.ToString("F0", CultureInfo.InvariantCulture);
}
