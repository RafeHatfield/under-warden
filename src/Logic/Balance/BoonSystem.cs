using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Applies depth boons to the player. Stateless utility class.
/// Exact port of PoC balance/depth_boons.py — same eligibility rules,
/// same stat mutations, same order of operations.
/// </summary>
public static class BoonSystem
{
    /// <summary>
    /// Award a depth boon on first arrival at the given depth.
    /// Returns the boon applied, or null if none (already visited, no mapping, disabled).
    ///
    /// Side effects when a boon fires:
    ///   - tracker.VisitedDepths gets the depth (even if no boon mapping exists)
    ///   - ApplyBoon mutates Fighter fields
    ///   - tracker.BoonsApplied gets the boon ID
    /// </summary>
    public static BoonDefinition? ApplyDepthBoonIfEligible(
        Entity player, int depth, BoonTracker tracker, IReadOnlyDictionary<int, BoonDefinition> boonTable)
    {
        if (tracker.DisableDepthBoons) return null;
        if (tracker.VisitedDepths.Contains(depth)) return null;

        // Mark as visited regardless of whether a boon exists for this depth.
        // Matches PoC: stats.visited_depths.add(depth) before boon lookup.
        tracker.VisitedDepths.Add(depth);

        if (!boonTable.TryGetValue(depth, out var boon)) return null;

        ApplyBoon(player, boon);
        tracker.BoonsApplied.Add(boon.BoonId);
        return boon;
    }

    /// <summary>
    /// Apply a boon's stat mutations to a player entity.
    /// Matches PoC apply_boon() — mutates Fighter fields in-place.
    /// </summary>
    public static void ApplyBoon(Entity player, BoonDefinition boon)
    {
        var fighter = player.Require<Fighter>();

        if (boon.HpBonus > 0)
            fighter.BoonMaxHpBonus += boon.HpBonus;

        if (boon.ImmediateHeal > 0)
            fighter.Hp = Math.Min(fighter.Hp + boon.ImmediateHeal, fighter.MaxHp);

        if (boon.AccuracyBonus != 0)
            fighter.Accuracy += boon.AccuracyBonus;

        if (boon.DefenseBonus != 0)
            fighter.BaseDefense += boon.DefenseBonus;

        if (boon.MinDamageBonus != 0)
            fighter.DamageMin += boon.MinDamageBonus;
    }
}
