using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Bot policy for net-arrow kiting scenarios.
/// Dispatched from ScenarioHarness when player_bot: "ranged_net_arrow" is set in YAML.
///
/// Decision priority:
///   1. Heal checks (inherited thresholds from BotConfig)
///   2. If any enemy at d3-6 with LoS: ShootAt nearest
///   3. If nearest enemy d≤2: move away (farthest open tile from all threats)
///   4. If nearest enemy d>6: move toward nearest until d==6
///   5. Fallback: melee attack if adjacent, else move toward nearest
///
/// Returns PlayerAction directly (not BotAction) because ShootAt is not in BotAction.
/// </summary>
public static class RangedNetArrowBot
{
    // Optimal firing band: d3-6 Chebyshev (matches plan spec exactly)
    private const int BandMin = 3;
    private const int BandMax = 6;
    private const int MaxRange = 8;   // d > MaxRange → denied by RangedCombatService

    /// <summary>
    /// Decide the bot's action this turn.
    /// Returns a PlayerAction directly — the harness calls this instead of BotBrain.Decide.
    /// </summary>
    public static PlayerAction Decide(
        Entity player,
        Fighter playerFighter,
        Inventory? inventory,
        List<Entity> monsters,
        GameMap map,
        GameState state)
    {
        var alive = monsters.Where(m => m.Get<Fighter>()?.IsAlive == true).ToList();
        if (alive.Count == 0)
            return PlayerAction.Wait;

        double hpFraction = playerFighter.MaxHp > 0
            ? (double)playerFighter.Hp / playerFighter.MaxHp
            : 0.0;

        // 1. Panic heal
        if (hpFraction <= BotConfig.PanicThreshold && hpFraction < 1.0 && HasHealingPotion(inventory))
            return PlayerAction.UseItem();

        // 2. Threshold heal
        if (hpFraction <= BotConfig.HealThreshold && HasHealingPotion(inventory))
            return PlayerAction.UseItem();

        // 3. Shoot if any enemy in optimal band (d3-6) with LoS
        var inBand = alive
            .Where(m =>
            {
                int d = player.ChebyshevDistanceTo(m.X, m.Y);
                return d >= BandMin && d <= BandMax && state.Map.HasLineOfSight(player.X, player.Y, m.X, m.Y);
            })
            .OrderBy(m => player.ChebyshevDistanceTo(m.X, m.Y))
            .FirstOrDefault();

        if (inBand != null)
            return PlayerAction.ShootAt(inBand);

        // 4. Too close (d≤2): back away, then shoot if now in band
        var nearest = FindNearest(player, alive);
        if (nearest != null)
        {
            int nearestDist = player.ChebyshevDistanceTo(nearest.X, nearest.Y);

            if (nearestDist <= 2)
            {
                if (nearestDist == 1)
                {
                    // Adjacent: shoot first, accepting retaliation-first penalty.
                    // A kiting archer at point-blank has limited options — firing is better
                    // than closing to melee which gives up the ranged advantage entirely.
                    // This also exercises the retaliation mechanic reliably.
                    if (state.Map.HasLineOfSight(player.X, player.Y, nearest.X, nearest.Y))
                        return PlayerAction.ShootAt(nearest);
                }
                else
                {
                    // d==2: close enough to be uncomfortable — try to back off.
                    var backoffTile = FindBackoffTile(player, alive, map);
                    if (backoffTile.HasValue)
                        return PlayerAction.MoveTo(backoffTile.Value.X, backoffTile.Value.Y);
                }
            }

            // 5. Shoot at penalty range (d7-8) — accept the damage penalty.
            //    Also attempt at d>8: RangedCombatService will deny it and record the denial
            //    counter. Combat proceeds normally because the aware orc chases each turn.
            if (nearestDist >= BandMin && state.Map.HasLineOfSight(player.X, player.Y, nearest.X, nearest.Y))
                return PlayerAction.ShootAt(nearest);

            // 6. Too far (d>8 and LoS blocked, or d<3): move toward nearest.
            if (nearestDist > BandMax)
                return PlayerAction.MoveToward(nearest);
        }

        // 6. Fallback: attack adjacent or move toward nearest
        var adjacent = alive.Where(m => player.ChebyshevDistanceTo(m.X, m.Y) <= 1).ToList();
        if (adjacent.Count > 0)
            return PlayerAction.Attack(adjacent[0]);

        if (nearest != null)
            return PlayerAction.MoveToward(nearest);

        return PlayerAction.Wait;
    }

    /// <summary>
    /// Find the adjacent walkable tile that maximizes the minimum Chebyshev distance
    /// from any alive enemy. Used when backing off from d≤2 threats.
    /// Returns null if no suitable tile found (cornered).
    /// </summary>
    private static (int X, int Y)? FindBackoffTile(Entity player, List<Entity> alive, GameMap map)
    {
        (int X, int Y)? best = null;
        double bestMinDist = -1;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = player.X + dx;
                int ny = player.Y + dy;
                if (!map.CanMoveTo(nx, ny)) continue;

                // Minimum Chebyshev distance to any enemy from this tile
                double minDist = alive
                    .Min(m => (double)Math.Max(Math.Abs(m.X - nx), Math.Abs(m.Y - ny)));

                if (minDist > bestMinDist)
                {
                    bestMinDist = minDist;
                    best = (nx, ny);
                }
            }
        }

        return best;
    }

    private static Entity? FindNearest(Entity from, List<Entity> candidates)
    {
        Entity? nearest = null;
        double bestDist = double.MaxValue;
        foreach (var c in candidates)
        {
            double d = from.DistanceTo(c.X, c.Y);
            if (d < bestDist) { bestDist = d; nearest = c; }
        }
        return nearest;
    }

    private static bool HasHealingPotion(Inventory? inventory)
    {
        if (inventory == null) return false;
        return inventory.FindFirst(item => item.Get<Consumable>()?.IsHealing == true) != null;
    }
}
