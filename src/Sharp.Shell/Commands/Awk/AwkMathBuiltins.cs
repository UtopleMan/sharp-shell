namespace Sharp.Shell.Commands.Awk;

// The numeric builtins, and the random number generator behind `rand`/`srand`.
//
// The generator is owned rather than borrowed because no two awks agree on theirs, so matching one
// would only mean failing against the other. It is the 48-bit linear congruential generator from
// `drand48`, which makes `srand(n)` reproducible — that reproducibility, not the sequence itself, is
// what the tests pin. The differential suite leaves `rand` alone.
internal sealed class AwkMathBuiltins
{
    private const ulong Multiplier = 0x5DEECE66D;

    private const ulong Increment = 0xB;

    private const ulong Modulus = 1UL << 48;

    private ulong state;

    private double seed;

    public AwkMathBuiltins() => Seed(0);

    public double Random()
    {
        state = ((state * Multiplier) + Increment) % Modulus;
        return state / (double)Modulus;
    }

    // srand returns the seed it replaced, which is how a program saves and restores a sequence.
    public double Seed(double value)
    {
        double previous = seed;
        seed = value;
        state = ((ulong)(long)value << 16) | 0x330E;

        return previous;
    }

    public double SeedFromClock() => Seed(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond);

    public static double Apply(AwkBuiltin builtin, double value) => builtin switch
    {
        AwkBuiltin.Sin => Math.Sin(value),
        AwkBuiltin.Cos => Math.Cos(value),
        AwkBuiltin.Exp => Math.Exp(value),
        AwkBuiltin.Log => Math.Log(value),
        AwkBuiltin.Sqrt => Math.Sqrt(value),
        _ => Math.Truncate(value),
    };
}
