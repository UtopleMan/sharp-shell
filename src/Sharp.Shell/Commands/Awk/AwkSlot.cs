namespace Sharp.Shell.Commands.Awk;

// One variable, which awk decides is a scalar or an array on first use rather than at declaration.
//
// Arrays pass to a function by reference and scalars by value, and a parameter that is still
// undecided has to be able to become either. The link is what makes that work: a callee's parameter
// keeps its own scalar value but shares whatever array its caller's variable ends up with, so
// `function fill(a) { a[1] = 5 }` fills the caller's array without `fill(n) { n = 5 }` touching the
// caller's number.
internal sealed class AwkSlot
{
    private AwkSlot? sharesArrayWith;

    private AwkArray? array;

    public AwkValue Value { get; set; }

    public bool HasArray => Root().array is not null;

    public AwkArray Array => Root().array ??= new AwkArray();

    public void ShareArrayWith(AwkSlot other) => sharesArrayWith = other;

    private AwkSlot Root() => sharesArrayWith?.Root() ?? this;
}
