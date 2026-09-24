namespace UnderWarden.Logic.Combat;

/// <summary>
/// Static helpers for combat calculations. Pure math, no state.
/// </summary>
public static class CombatMath
{
    /// <summary>
    /// D&amp;D-style ability modifier: (stat - 10) / 2, rounded toward negative infinity.
    /// 8-9 = -1, 10-11 = 0, 12-13 = +1, 14-15 = +2, 16-17 = +3, 18 = +4.
    /// </summary>
    public static int StatModifier(int stat)
    {
        int diff = stat - 10;
        // Floor division: round toward negative infinity (matches Python // operator)
        return diff >= 0 ? diff / 2 : (diff - 1) / 2;
    }

    /// <summary>
    /// Roll damage within a range using the provided RNG.
    /// Returns a value in [min, max] inclusive.
    /// If min > max or both are 0, returns 0.
    /// </summary>
    public static int RollDamage(Core.SeededRandom rng, int min, int max)
    {
        if (min <= 0 && max <= 0) return 0;
        if (min > max) return 0;
        return rng.Next(min, max + 1); // Next is exclusive on upper bound
    }
}
