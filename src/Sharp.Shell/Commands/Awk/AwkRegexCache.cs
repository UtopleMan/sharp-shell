using Sharp.Shell.Text;

namespace Sharp.Shell.Commands.Awk;

// Every pattern awk uses, compiled once. A dynamic pattern the translator will not vouch for is a
// run-time failure rather than a silent difference, and it fails at the point it resolves — which
// may be after earlier records have already printed. Failing loudly beats buffering the whole stream
// to make the failure tidy.
internal sealed class AwkRegexCache
{
    private readonly Dictionary<string, AwkRegex> compiled = new(StringComparer.Ordinal);

    public AwkRegex Compile(string pattern)
    {
        if (compiled.TryGetValue(pattern, out AwkRegex? existing))
        {
            return existing;
        }

        if (!AwkRegex.TryCreate(pattern, out AwkRegex? fresh, out string? refusal))
        {
            throw new AwkRuntimeException($"unsupported construct: {refusal}");
        }

        compiled[pattern] = fresh!;
        return fresh!;
    }
}
