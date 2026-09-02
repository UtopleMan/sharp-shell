namespace Sharp.Shell.Commands;

// Where an invocation's file operands sit in its argument list, split by direction. Positions
// rather than values, because classification reads a word before it is expanded: the host needs the
// file `sed -n 1p "$f"` will really open, which only the word itself can answer, and only the
// declared positions have to be literal for that answer to exist.
public sealed record OperandPositions(IReadOnlyList<int> Reads, IReadOnlyList<int> Writes)
{
    public static OperandPositions None { get; } = new([], []);

    public static OperandPositions Reading(IReadOnlyList<int> reads) => new(reads, []);

    public static OperandPositions Writing(IReadOnlyList<int> writes) => new([], writes);
}
