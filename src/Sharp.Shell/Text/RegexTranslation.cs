namespace Sharp.Shell.Text;

public enum RegexDialect
{
    BasicPosix,
    ExtendedPosix,
}

// Never throws: an untranslatable pattern is a value, not an exception, because the caller turns it
// into a refusal that escalates the command line rather than into an error.
public sealed record RegexTranslation(string? Pattern, string? RefusalReason)
{
    public bool IsTranslated => Pattern is not null;

    public static RegexTranslation Translated(string pattern) => new(pattern, null);

    public static RegexTranslation Refuse(string reason) => new(null, reason);
}
