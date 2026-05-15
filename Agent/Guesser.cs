using Shared;

namespace Agent;

/// <summary>
/// Encapsulates the guessing loop so the Program class stays small.
/// Uses a per-instance Random seeded from a high-entropy source — important
/// because two agents started in the same millisecond would otherwise
/// produce identical sequences with the legacy default seed.
/// </summary>
public sealed class Guesser
{
    private readonly Random _rng;
    private readonly int _min;
    private readonly int _max;

    public Guesser(int seed, int min = Protocol.MinNumber, int max = Protocol.MaxNumber)
    {
        _rng = new Random(seed);
        _min = min;
        _max = max;
    }

    /// <summary>
    /// Repeatedly draws from [min, max] until a value matches the target.
    /// Yields each guess so the caller can either send it on the wire or just count attempts.
    /// </summary>
    public IEnumerable<int> Guesses()
    {
        while (true)
            yield return _rng.Next(_min, _max + 1);
    }

    /// <summary>
    /// Convenience wrapper: keeps drawing until the target is hit, returns attempts taken.
    /// </summary>
    public int GuessUntil(int target)
    {
        int attempts = 0;
        foreach (int g in Guesses())
        {
            attempts++;
            if (g == target) return attempts;
        }
        return attempts; // unreachable
    }
}
