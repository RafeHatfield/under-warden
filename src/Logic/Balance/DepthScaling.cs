namespace UnderWarden.Logic.Balance;

/// <summary>
/// Multipliers applied to monster stats at spawn time based on dungeon depth.
/// HP rounds up (ceiling), damage and to-hit round half-up.
/// </summary>
public readonly record struct ScalingMultipliers(double Hp, double ToHit, double Damage);

/// <summary>
/// Depth-based stat scaling for monsters. Ported from Python prototype's
/// balance/depth_scaling.py. Applied once at spawn time, not dynamically.
///
/// Default curve (most monsters):
///   Depth 1-2: 1.0x all stats
///   Depth 3-4: HP 1.08x, ToHit 1.06x, Damage 1.00x
///   Depth 5-6: HP 1.25x, ToHit 1.12x, Damage 1.05x
///   Depth 7-8: HP 1.35x, ToHit 1.17x, Damage 1.10x
///   Depth 9+:  HP 1.45x, ToHit 1.22x, Damage 1.15x
///
/// Zombie curve (conservative, avoids amplifying depth 5 spike):
///   Depth 1-6: 1.0x (no scaling)
///   Depth 7-8: HP 1.10x, ToHit 1.05x, Damage 1.00x
///   Depth 9+:  HP 1.20x, ToHit 1.10x, Damage 1.05x
/// </summary>
public static class DepthScaling
{
    private static readonly ScalingMultipliers[] DefaultCurve =
    [
        new(1.00, 1.00, 1.00), // band 0: depth 1-2
        new(1.08, 1.06, 1.00), // band 1: depth 3-4
        new(1.25, 1.12, 1.05), // band 2: depth 5-6
        new(1.35, 1.17, 1.10), // band 3: depth 7-8
        new(1.45, 1.22, 1.15), // band 4: depth 9+
    ];

    private static readonly ScalingMultipliers[] ZombieCurve =
    [
        new(1.00, 1.00, 1.00), // band 0: depth 1-2
        new(1.00, 1.00, 1.00), // band 1: depth 3-4
        new(1.00, 1.00, 1.00), // band 2: depth 5-6
        new(1.10, 1.05, 1.00), // band 3: depth 7-8
        new(1.20, 1.10, 1.05), // band 4: depth 9+
    ];

    /// <summary>Get the depth band index (0-4) for a given depth.</summary>
    public static int GetBand(int depth) => Math.Clamp((depth - 1) / 2, 0, 4);

    /// <summary>Get scaling multipliers for a depth using the default curve.</summary>
    public static ScalingMultipliers GetDefault(int depth) => DefaultCurve[GetBand(depth)];

    /// <summary>Get scaling multipliers for a depth using the zombie curve.</summary>
    public static ScalingMultipliers GetZombie(int depth) => ZombieCurve[GetBand(depth)];

    /// <summary>
    /// Get multipliers for a depth, selecting curve based on monster tags.
    /// Zombies (tag "zombie") use the conservative curve.
    /// </summary>
    public static ScalingMultipliers GetForTags(int depth, IEnumerable<string>? tags)
    {
        if (tags != null && tags.Contains("zombie"))
            return GetZombie(depth);
        return GetDefault(depth);
    }

    /// <summary>
    /// Apply scaling to a base HP value. Rounds up (ceiling).
    /// </summary>
    public static int ScaleHp(int baseHp, double multiplier)
        => (int)Math.Ceiling(baseHp * multiplier);

    /// <summary>
    /// Apply scaling to a damage or to-hit value. Rounds half-up.
    /// </summary>
    public static int ScaleStat(int baseStat, double multiplier)
        => (int)Math.Round(baseStat * multiplier, MidpointRounding.AwayFromZero);
}
