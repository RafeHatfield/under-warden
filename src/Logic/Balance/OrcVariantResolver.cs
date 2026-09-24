using UnderWarden.Logic.Core;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Resolves a generic "orc" spawn request to a specific variant based on dungeon depth.
/// Called by EntityPlacer when the weighted monster pool selects the "orc" entry.
///
/// This keeps spawn_weight on the base "orc" entry while routing the actual creation
/// to the appropriate variant — the distribution widens as the player descends.
///
/// Ported from PoC spawn_service.py: _resolve_orc_variant().
/// </summary>
public static class OrcVariantResolver
{
    /// <summary>
    /// Returns the monster type ID to actually spawn when "orc" is selected from the pool.
    /// Possible return values: "orc", "orc_brute", "orc_shaman", "orc_skirmisher".
    /// </summary>
    public static string Resolve(int depth, SeededRandom rng)
    {
        // Depth 1: only base orcs.
        if (depth < 2)
            return "orc";

        // Depth 2: small chance of brute (5%) or shaman (3%), otherwise base orc.
        if (depth == 2)
        {
            double roll = rng.NextDouble();
            if (roll < 0.05) return "orc_brute";
            if (roll < 0.08) return "orc_shaman";
            return "orc";
        }

        // Depth 3: skirmishers appear (7.5%), brute 7.5%, shaman 7%.
        if (depth == 3)
        {
            double roll = rng.NextDouble();
            if (roll < 0.075) return "orc_skirmisher";
            if (roll < 0.150) return "orc_brute";
            if (roll < 0.220) return "orc_shaman";
            return "orc";
        }

        // Depth 4–5: skirmisher rate rises to 12.5%, brute/shaman 10% each.
        if (depth <= 5)
        {
            double roll = rng.NextDouble();
            if (roll < 0.125) return "orc_skirmisher";
            if (roll < 0.225) return "orc_brute";
            if (roll < 0.325) return "orc_shaman";
            return "orc";
        }

        // Depth 6+: skirmisher 17.5%, brute/shaman 10% each.
        {
            double roll = rng.NextDouble();
            if (roll < 0.175) return "orc_skirmisher";
            if (roll < 0.275) return "orc_brute";
            if (roll < 0.375) return "orc_shaman";
            return "orc";
        }
    }
}
