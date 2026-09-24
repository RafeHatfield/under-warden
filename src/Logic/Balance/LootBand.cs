namespace UnderWarden.Logic.Balance;

/// <summary>
/// Five-tier dungeon band system aligned with the PoC.
///
/// Each band covers 5 floors and drives loot density, ring gating,
/// and pity thresholds. The design intent is intentional early-game
/// scarcity (B1 is sparse) ramping to full loot density at B3+.
///
/// B1: depths  1-5   (early game, scarcity, learning)
/// B2: depths  6-10  (early-mid, slight easing)
/// B3: depths 11-15  (mid game, full density baseline)
/// B4: depths 16-20  (mid-late, tactical depth)
/// B5: depths 21-25  (endgame, gear optimization)
/// </summary>
public enum LootBand { B1 = 1, B2, B3, B4, B5 }

/// <summary>
/// Static helpers for the LootBand system.
/// All multiplier tables are PoC-exact from balance/loot_tags.py.
/// </summary>
public static class LootBands
{
    /// <summary>Map a dungeon depth (1-25+) to a loot band.</summary>
    public static LootBand FromDepth(int depth) => depth switch
    {
        <= 5  => LootBand.B1,
        <= 10 => LootBand.B2,
        <= 15 => LootBand.B3,
        <= 20 => LootBand.B4,
        _     => LootBand.B5,
    };

    /// <summary>
    /// Item density multiplier: fraction of eligible rooms that generate a loot item.
    /// B1-B2 are intentionally sparse — pity provides the safety net.
    /// PoC: BAND_ITEM_DENSITY_MULTIPLIER in balance/loot_tags.py.
    /// </summary>
    public static readonly IReadOnlyDictionary<LootBand, double> DensityMultiplier =
        new Dictionary<LootBand, double>
        {
            [LootBand.B1] = 0.35,
            [LootBand.B2] = 0.45,
            [LootBand.B3] = 1.0,
            [LootBand.B4] = 1.0,
            [LootBand.B5] = 1.0,
        };

    /// <summary>
    /// Healing (and defensive) category weight multiplier per band.
    /// Low healing in B1 raises early-game tension; pity backs it up.
    /// PoC: HEALING_BAND_MULTIPLIER in balance/loot_tags.py.
    /// </summary>
    public static readonly IReadOnlyDictionary<LootBand, double> HealingMultiplier =
        new Dictionary<LootBand, double>
        {
            [LootBand.B1] = 0.25,
            [LootBand.B2] = 0.35,
            [LootBand.B3] = 1.0,
            [LootBand.B4] = 1.1,
            [LootBand.B5] = 1.1,
        };

    /// <summary>
    /// Rare (ring) category weight multiplier per band.
    /// Near-zero in B1 prevents early power spikes; ramps to full by B5.
    /// PoC: RARE_BAND_MULTIPLIER in balance/loot_tags.py.
    /// </summary>
    public static readonly IReadOnlyDictionary<LootBand, double> RareMultiplier =
        new Dictionary<LootBand, double>
        {
            [LootBand.B1] = 0.05,
            [LootBand.B2] = 0.15,
            [LootBand.B3] = 0.5,
            [LootBand.B4] = 0.8,
            [LootBand.B5] = 1.0,
        };
}
