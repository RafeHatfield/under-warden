namespace UnderWarden.Logic.Core;

/// <summary>
/// Deterministic random number generator. Same seed always produces the same sequence.
/// Wraps System.Random for now; can be replaced with a more portable PRNG if needed.
///
/// Mid-run save/resume (M1.4): the generator tracks a <see cref="CallCount"/> — the number of
/// draws taken since construction. A save stores (Seed, CallCount); a load calls
/// <see cref="Restore(int, long)"/>, which reconstructs Random(seed) and burns CallCount raw draws
/// to restore the exact sequence position. This works because seeded System.Random is the legacy
/// algorithm, where every draw method consumes exactly ONE internal sample at game-scale ranges —
/// so replaying CallCount single-sample draws lands on the identical internal state. See
/// docs/systems/save_resume_boundary.md. (M2 replaces this with an explicit-state PRNG.)
/// </summary>
public sealed class SeededRandom
{
    private readonly Random _rng;

    public int Seed { get; }

    /// <summary>
    /// Number of draws taken since construction. Incremented by every Next/NextDouble/NextFloat.
    /// Persisted for mid-run save/resume; burned back in on <see cref="Restore(int, long)"/>.
    /// </summary>
    public long CallCount { get; private set; }

    public SeededRandom(int seed = 1337)
    {
        Seed = seed;
        _rng = new Random(seed);
    }

    /// <summary>
    /// Reconstruct a generator at a saved sequence position: seed the legacy PRNG, then burn
    /// <paramref name="callCount"/> raw single-sample draws so the next draw matches the original
    /// run exactly. Burning uses the no-arg internal sample, which advances state identically to
    /// any of the wrapper draw methods (all consume one internal sample at game-scale ranges).
    /// </summary>
    public static SeededRandom Restore(int seed, long callCount)
    {
        if (callCount < 0)
            throw new ArgumentOutOfRangeException(nameof(callCount), callCount, "CallCount cannot be negative.");

        var restored = new SeededRandom(seed);
        for (long i = 0; i < callCount; i++)
            restored._rng.Next();           // one internal sample per burn — mirrors every wrapper draw
        restored.CallCount = callCount;
        return restored;
    }

    /// <summary>Returns a non-negative random integer less than maxExclusive.</summary>
    public int Next(int maxExclusive)
    {
        CallCount++;
        return _rng.Next(maxExclusive);
    }

    /// <summary>Returns a random integer in [minInclusive, maxExclusive).</summary>
    public int Next(int minInclusive, int maxExclusive)
    {
        // Continuity guard (M1.4): the save/restore burn assumes exactly one internal sample per
        // call. System.Random.Next(min,max) only stays single-sample while the range fits well
        // within int range; the two-sample path triggers near int.MaxValue. Game-scale ranges are
        // tiny, so this never fires in practice — it fails loud if a caller ever breaks the premise.
        System.Diagnostics.Debug.Assert(
            (long)maxExclusive - minInclusive < int.MaxValue / 2,
            "SeededRandom.Next(min,max): range too large — breaks single-sample save/restore continuity.");
        CallCount++;
        return _rng.Next(minInclusive, maxExclusive);
    }

    /// <summary>Returns a random double in [0.0, 1.0).</summary>
    public double NextDouble()
    {
        CallCount++;
        return _rng.NextDouble();
    }

    /// <summary>Returns a random float in [0.0, 1.0).</summary>
    public float NextFloat()
    {
        CallCount++;
        return (float)_rng.NextDouble();
    }
}
