using System.Text.RegularExpressions;

namespace Sharp.Shell.Text;

// POSIX basic and extended regular expressions translated to .NET patterns, refusing anything the
// translation cannot prove equivalent.
//
// The difference is silent and therefore dangerous: `a\+` repeats in BRE and is a literal plus in
// .NET, `foo(bar)` is literal parentheses in BRE and a group in .NET, `[[:digit:]]` is a class in
// POSIX and a character set in .NET. Handing a POSIX pattern straight to Regex produces a wrong
// answer that looks like a right one, which is the failure the sandbox's conservative-applet rule
// exists to prevent. An untranslatable pattern becomes a refusal, and the command escalates.
//
// Character classes are the C locale (ASCII), matching a library built with InvariantGlobalization.
// Dot and negated brackets match a newline unless multi-line mode is on, which is what GNU sed does
// with the M flag.
public static class PosixRegexTranslator
{
    // usesMatchExtent says whether the caller cares *where* the match ends or only whether there is
    // one. sed's `s` replaces the matched text and so depends on the extent; grep and sed's
    // addresses only ever ask IsMatch, and for them the leftmost-longest difference cannot change
    // the answer — so the alternation rule, which would refuse `error\|warn\b`, does not apply.
    public static RegexTranslation Translate(
        string pattern,
        RegexDialect dialect,
        bool allowsGnuExtensions = true,
        bool isMultiline = false,
        bool usesMatchExtent = true)
    {
        PosixRegexParser parser = new(pattern, dialect, allowsGnuExtensions, isMultiline);
        RegexNode? parsed = parser.Parse();

        if (parsed is null)
        {
            return RegexTranslation.Refuse(parser.Refusal ?? "the pattern could not be translated");
        }

        string? unsafeAlternation = usesMatchExtent ? AlternationSafety.Check(parsed) : null;

        if (unsafeAlternation is not null)
        {
            return RegexTranslation.Refuse(unsafeAlternation);
        }

        string emitted = RegexEmitter.Emit(parsed);

        return IsValidDotNetPattern(emitted)
            ? RegexTranslation.Translated(emitted)
            : RegexTranslation.Refuse("the translated pattern is not a valid .NET regular expression");
    }

    // awk's entry point: an extended POSIX pattern with no GNU extensions, where an alternation that
    // sed would refuse is translated and flagged instead. Multi-line mode does not arise — awk's `^`
    // and `$` always mean the ends of the subject string.
    public static PosixRegexProgram TranslateForExtent(string pattern)
    {
        PosixRegexParser parser = new(
            pattern,
            RegexDialect.ExtendedPosix,
            allowsGnuExtensions: false,
            isMultiline: false);

        RegexNode? parsed = parser.Parse();

        if (parsed is null)
        {
            return PosixRegexProgram.Refuse(parser.Refusal ?? "the pattern could not be translated");
        }

        string emitted = RegexEmitter.Emit(parsed);

        if (!IsValidDotNetPattern(emitted))
        {
            return PosixRegexProgram.Refuse("the translated pattern is not a valid .NET regular expression");
        }

        LongestMatchFinder? extent = AlternationSafety.IsExtentSafe(parsed)
            ? null
            : LongestMatchFinder.For(parsed);

        return PosixRegexProgram.Translated(emitted, extent);
    }

    private static bool IsValidDotNetPattern(string pattern)
    {
        try
        {
            _ = new Regex(pattern);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
