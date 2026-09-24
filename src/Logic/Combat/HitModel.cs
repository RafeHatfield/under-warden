namespace UnderWarden.Logic.Combat;

/// <summary>
/// Pure math for hit chance calculation. No state, no dependencies.
///
/// Formula: hit_chance = BASE_HIT + (accuracy - evasion) * STEP
/// Clamped to [MIN_HIT, MAX_HIT].
///
/// Each point of accuracy/evasion difference = 5% hit chance shift.
/// </summary>
public static class HitModel
{
    public const double BaseHit = 0.75;
    public const double Step = 0.05;
    public const double MinHit = 0.05;
    public const double MaxHit = 0.95;

    public const int DefaultAccuracy = 2;
    public const int DefaultEvasion = 1;

    /// <summary>
    /// Compute hit probability from attacker accuracy vs defender evasion.
    /// Returns a value in [MinHit, MaxHit].
    /// </summary>
    public static double ComputeHitChance(int attackerAccuracy, int defenderEvasion)
    {
        double raw = BaseHit + (attackerAccuracy - defenderEvasion) * Step;
        return Math.Clamp(raw, MinHit, MaxHit);
    }
}
