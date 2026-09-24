using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Bot decision-making for the scenario harness. Supports five named personas via
/// BotPersonaConfig. Default persona is "balanced" — behavior identical to the pre-persona
/// static implementation.
///
/// Two usage modes:
///   1. Static (legacy, no stuck detection): BotBrain.Decide(...) constructs a transient
///      instance per call. Stuck state is per-call — never fires. Existing tests use this path.
///   2. Instance (per-run, with stuck detection): new BotBrain(persona).Decide(...)
///      Harnesses construct one instance per run so stuck state persists across turns.
///      The graphical bot driver creates one instance per Enable() call.
///
/// Decision priority order (maps to PoC bot_brain.py decide_action pipeline):
///   1. Panic heal     — HP at panic threshold AND 2+ adjacent enemies AND has potion
///   2. Threshold heal — HP below base threshold AND (allow_combat_healing OR no adjacent)
///   3. Opportunistic loot — floor potion nearby AND no adjacent enemies (loot_priority > 0)
///   4. Retreat to choke  — 2+ adjacent enemies AND HP below retreat_hp_threshold
///   5. Avoid-combat detour — avoid_combat==true AND nearby enemy within engagement range
///   6. Engage / move — chase nearest enemy within combat_engagement_distance (Manhattan)
///
/// Distance note: CombatEngagementDistance uses Manhattan distance (abs(dx)+abs(dy)),
/// matching the PoC's bot_brain.py semantics. All other distance checks in this class
/// (adjacency, loot pickup, choke search) use Chebyshev. This asymmetry is intentional —
/// engagement-distance values (4, 5, 6, 8, 12) were tuned against Manhattan in the PoC
/// and must NOT be compared against Chebyshev equivalents.
/// </summary>
public sealed class BotBrain
{
    // ── Stuck detection state (instance-only — static path never uses these) ──

    // _stuckCounter: consecutive turns the bot has not moved (player position unchanged).
    // Increments when player position is unchanged regardless of what the target does.
    //
    // PRIOR BUG (fixed): the counter previously required BOTH player pos AND target pos
    // to be unchanged. A wandering target continually reset the counter, allowing the bot
    // to issue the same blocked move for 990+ turns while the stuck thresholds never fired.
    // The correct invariant: "the bot made no progress" = player didn't move.
    //
    // Drop threshold (4): drop current target, wait one turn. Counter continues climbing.
    // Force-descend threshold (12): bot is truly stuck; harness should navigate to stair
    //   and force-descend even if the floor isn't clear (signals as ForceDescend, not AbortRun,
    //   so the run continues and the next floor gets a fresh attempt).
    // Abort threshold (20): fallback if even stair navigation fails.
    private int _stuckCounter;
    private (int X, int Y)? _lastPlayerPos;
    private Entity? _stuckDroppedTarget;

    private const int StuckDropTargetThreshold   = 4;
    private const int StuckForceDescendThreshold = 12;
    private const int StuckAbortRunThreshold     = 20;

    private readonly BotPersonaConfig _persona;

    public BotBrain(BotPersonaConfig? persona = null)
    {
        _persona = persona ?? BotPersonaRegistry.Get("balanced");
    }

    // ── Instance Decide ───────────────────────────────────────────────────────

    /// <summary>
    /// Instance-mode decision. Stuck state persists across calls — use when the same
    /// BotBrain instance is reused across multiple turns of the same run.
    /// </summary>
    public BotAction Decide(
        Entity player,
        Fighter playerFighter,
        Inventory? inventory,
        List<Entity> monsters,
        GameMap map,
        BotDecisionContext? context = null,
        IReadOnlyList<Entity>? floorItems = null)
        => DecideInternal(player, playerFighter, inventory, monsters, map, context, floorItems, _persona, this);

    // ── Static Decide (legacy surface, no stuck detection) ────────────────────

    /// <summary>
    /// Static convenience path. Constructs a transient BotBrain instance per call.
    /// Stuck detection NEVER fires on this path — state is discarded after each call.
    /// This is intentional: existing tests and callers that don't care about stuck behavior
    /// continue to work unchanged.
    ///
    /// To enable stuck detection, create a BotBrain instance and use the instance Decide().
    /// </summary>
    public static BotAction Decide(
        Entity player,
        Fighter playerFighter,
        Inventory? inventory,
        List<Entity> monsters,
        GameMap map,
        BotDecisionContext? context = null,
        IReadOnlyList<Entity>? floorItems = null,
        BotPersonaConfig? persona = null)
    {
        // Transient instance — stuck counters are allocated but never survive past this call.
        var transient = new BotBrain(persona);
        return transient.Decide(player, playerFighter, inventory, monsters, map, context, floorItems);
    }

    // ── Core decision logic ────────────────────────────────────────────────────

    private static BotAction DecideInternal(
        Entity player,
        Fighter playerFighter,
        Inventory? inventory,
        List<Entity> monsters,
        GameMap map,
        BotDecisionContext? context,
        IReadOnlyList<Entity>? floorItems,
        BotPersonaConfig persona,
        BotBrain? stateHolder)
    {
        var aliveMonsters = GetAlive(monsters);
        if (aliveMonsters.Count == 0)
        {
            EmitDecision(context, player, playerFighter, inventory, aliveMonsters, "Wait", "no_targets", persona);
            return BotAction.None;
        }

        double hpFraction = (double)playerFighter.Hp / playerFighter.MaxHp;
        var adjacent = GetAdjacent(player, aliveMonsters);

        // ── Position-based stuck detection ─────────────────────────────────────
        // Runs BEFORE any rule fires so it covers all code paths: loot-chase, retreat,
        // enemy-chase, and avoid-combat all pass through here. The counter increments any
        // turn the player didn't move, regardless of WHAT the bot was trying to do. Only
        // attacks reset it (via the explicit ResetStuck call on the attack return path).
        stateHolder?.UpdateStuck(player);
        if (stateHolder != null)
        {
            if (stateHolder._stuckCounter >= StuckAbortRunThreshold)
            {
                EmitDecision(context, player, playerFighter, inventory, aliveMonsters, "Wait", "stuck_abort", persona);
                return BotAction.AbortRun;
            }
            if (stateHolder._stuckCounter >= StuckForceDescendThreshold)
            {
                EmitDecision(context, player, playerFighter, inventory, aliveMonsters, "Wait", "stuck_force_descend", persona);
                return BotAction.ForceDescend;
            }
            if (stateHolder._stuckCounter >= StuckDropTargetThreshold)
            {
                EmitDecision(context, player, playerFighter, inventory, aliveMonsters, "Wait", "stuck_drop_target", persona);
                return BotAction.None;
            }
        }

        // 1. Panic heal — HP at panic threshold AND 2+ adjacent enemies AND has potion.
        //    PoC: requires multi-enemy pressure (panic_multi_enemy_count). This differs from
        //    the old C# impl that panicked on HP alone. The new rule is PoC-correct.
        if (hpFraction <= persona.PanicHpThreshold
            && hpFraction < 1.0
            && adjacent.Count >= persona.PanicMultiEnemyCount
            && HasUsableHealingPotion(inventory, playerFighter))
        {
            EmitDecision(context, player, playerFighter, inventory, aliveMonsters, "Heal", "panic_heal", persona);
            stateHolder?.ResetStuck(player);
            return BotAction.Heal;
        }

        // 2. Threshold heal — below BaseHealThreshold, gated on AllowCombatHealing.
        //    PoC: allow_combat_healing is true for all current personas. If false, only heals
        //    when no adjacent enemies.
        if (hpFraction <= persona.BaseHealThreshold
            && HasUsableHealingPotion(inventory, playerFighter)
            && (persona.AllowCombatHealing || adjacent.Count == 0))
        {
            EmitDecision(context, player, playerFighter, inventory, aliveMonsters, "Heal", "threshold_heal", persona);
            stateHolder?.ResetStuck(player);
            return BotAction.Heal;
        }

        // 3. Opportunistic loot — useful item nearby when not actively brawling.
        //    search radius: 3 for loot_priority==1, 6 for loot_priority==2.
        //    Skipped entirely when loot_priority==0 (aggressive, speedrunner).
        //    "Useful" = any consumable (potions, scrolls), wand, equippable (weapon/armour), or key.
        //    Previously only healing potions were targeted; the bot now picks up anything worth having.
        if (persona.LootPriority > 0 && adjacent.Count == 0 && floorItems != null)
        {
            int searchRadius = persona.LootPriority >= 2 ? 6 : 3;
            var nearbyItem = FindNearbyFloorItem(player, floorItems, searchRadius);
            if (nearbyItem != null)
            {
                EmitDecision(context, player, playerFighter, inventory, aliveMonsters, "MoveTo", "loot_potion", persona);
                return BotAction.MoveTo(nearbyItem.X, nearbyItem.Y);
            }
        }

        // 4. Retreat to choke point — when 2+ enemies adjacent AND HP below retreat threshold.
        //    Only fires for personas where AvoidCombat==false. avoid_combat personas use rule 5.
        if (!persona.AvoidCombat && adjacent.Count >= 2 && hpFraction < persona.RetreatHpThreshold)
        {
            if (!AtChokePoint(player, map))
            {
                var choke = FindChokePoint(player, map);
                if (choke.HasValue)
                {
                    EmitDecision(context, player, playerFighter, inventory, aliveMonsters, "MoveTo", "retreat_to_choke", persona);
                    return BotAction.MoveTo(choke.Value.x, choke.Value.y);
                }
            }
            // Already at choke or no choke found — fall through and fight
        }

        // 5. Avoid-combat detour — only fires when persona.AvoidCombat == true.
        //    If the nearest enemy is NOT adjacent but within CombatEngagementDistance
        //    (Manhattan), take one step away from it. If no walkable retreat tile, fall through.
        //    PoC: "kiting" behavior for cautious/speedrunner.
        if (persona.AvoidCombat)
        {
            var nearest = FindNearest(player, aliveMonsters);
            if (nearest != null)
            {
                int manhattanDist = Math.Abs(nearest.X - player.X) + Math.Abs(nearest.Y - player.Y);
                bool isAdjacent = player.ChebyshevDistanceTo(nearest.X, nearest.Y) <= 1;

                if (!isAdjacent && manhattanDist <= persona.CombatEngagementDistance)
                {
                    // Step away: direction is (player - enemy), normalized to one step
                    int retreatDx = Math.Sign(player.X - nearest.X);
                    int retreatDy = Math.Sign(player.Y - nearest.Y);
                    int retreatX = player.X + retreatDx;
                    int retreatY = player.Y + retreatDy;

                    if (map.CanMoveTo(retreatX, retreatY))
                    {
                        EmitDecision(context, player, playerFighter, inventory, aliveMonsters, "MoveTo", "avoid_combat_detour", persona);
                        stateHolder?.ResetStuck(player);
                        return BotAction.MoveTo(retreatX, retreatY);
                    }
                    // No walkable retreat tile — fall through to engage
                }
            }
        }

        // 5. Attack adjacent enemy — focus fire (or forced escalator priority for experiments).
        if (adjacent.Count > 0)
        {
            var target = PickTargetWithPriority(adjacent, persona);

            EmitDecision(context, player, playerFighter, inventory, aliveMonsters, "Attack", "attack_lowest_hp", persona);
            stateHolder?.ResetStuck(player);  // attack = progress; reset counter
            return BotAction.Attack(target);
        }

        // 6. Move toward nearest alive enemy.
        //    For non-avoid-combat personas (balanced, aggressive, greedy): always move toward the
        //    nearest enemy, regardless of distance. CombatEngagementDistance is meaningful only when
        //    paired with an EXPLORE state (as in the PoC's state machine). Without EXPLORE, applying
        //    an engagement distance cap would freeze the bot on large dungeon floors where all enemies
        //    start far away.
        //    For avoid-combat personas (cautious, speedrunner): rule 5 above handles the avoidance
        //    behavior for enemies within engagement distance. Enemies BEYOND engagement distance
        //    are ignored entirely — the bot Waits (PoC: avoid_combat skips non-adjacent enemies).
        //    Manhattan vs Chebyshev asymmetry note: FindNearest uses Euclidean distance (DistanceTo),
        //    which is consistent with "nearest" semantics. The engagement-distance gate uses Manhattan
        //    to match the PoC's bot_brain.py semantics.
        var nearestEnemy = FindNearestWithPriority(player, aliveMonsters, persona);
        if (nearestEnemy != null)
        {
            // Skip stuckDroppedTarget if alive (don't immediately re-chase just-dropped target)
            if (stateHolder?._stuckDroppedTarget != null
                && ReferenceEquals(nearestEnemy, stateHolder._stuckDroppedTarget)
                && (nearestEnemy.Get<Fighter>()?.IsAlive == true))
            {
                // Try to find an alternative target not in the dropped set
                var alternative = aliveMonsters
                    .Where(m => !ReferenceEquals(m, stateHolder._stuckDroppedTarget))
                    .Select(m => (Entity: m, Dist: Math.Abs(m.X - player.X) + Math.Abs(m.Y - player.Y)))
                    .OrderBy(t => t.Dist)
                    .Select(t => t.Entity)
                    .FirstOrDefault();

                if (alternative == null)
                {
                    // No alternative — clear the dropped target lock and fall through
                    stateHolder._stuckDroppedTarget = null;
                }
                else
                {
                    nearestEnemy = alternative;
                }
            }
            else if (stateHolder?._stuckDroppedTarget != null
                     && !(stateHolder._stuckDroppedTarget.Get<Fighter>()?.IsAlive == true))
            {
                // Dropped target died — clear the lock
                stateHolder._stuckDroppedTarget = null;
            }

            if (nearestEnemy != null)
            {
                // AvoidCombat: skip enemies beyond engagement distance (PoC: avoid_combat ignores
                // non-adjacent enemies entirely when outside engagement range — bot waits/explores).
                if (persona.AvoidCombat)
                {
                    int manhattanToNearest = Math.Abs(nearestEnemy.X - player.X) + Math.Abs(nearestEnemy.Y - player.Y);
                    if (manhattanToNearest > persona.CombatEngagementDistance)
                    {
                        // Enemy too far for avoid_combat persona — wait
                        EmitDecision(context, player, playerFighter, inventory, aliveMonsters, "Wait", "avoid_combat_too_far", persona);
                        return BotAction.None;
                    }
                }

                EmitDecision(context, player, playerFighter, inventory, aliveMonsters, "MoveToward", "move_to_nearest", persona);
                return BotAction.Move(nearestEnemy);
            }
        }

        EmitDecision(context, player, playerFighter, inventory, aliveMonsters, "Wait", "no_targets", persona);
        return BotAction.None;
    }

    // ── Stuck detection helpers ────────────────────────────────────────────────

    /// <summary>
    /// Track player position for stuck detection. Increments the counter whenever the
    /// player has not moved, regardless of where the target is.
    ///
    /// Prior design checked both player AND target position, which meant a wandering monster
    /// reset the counter every turn it moved — allowing 990+ turn freezes while the bot
    /// issued the same blocked move. The correct signal is "did the bot move?" only.
    /// </summary>
    /// <summary>
    /// Track player position + target entity ID for stuck detection.
    ///
    /// Increment when BOTH player position AND target entity are unchanged. This catches
    /// the failure mode: player issues the same move toward the same enemy and doesn't progress.
    ///
    /// Using target entity ID (not position) means:
    /// - Wandering monster (same entity, moves around): counter STILL increments — the bot
    ///   is stuck on the same entity regardless of where it wanders. This is the original bug fix.
    /// - New target entity (a different enemy becomes the priority): counter resets — the bot
    ///   deserves a fresh attempt at the new target without carrying over stuck state.
    /// </summary>
    /// <summary>
    /// Increment the stuck counter when the bot hasn't moved. Called once per decision
    /// cycle, BEFORE any rule fires, so it covers all paths (loot, retreat, enemy-chase)
    /// equally. Attacks reset the counter (progress) separately via ResetStuck.
    /// </summary>
    private void UpdateStuck(Entity player)
    {
        var pos = (player.X, player.Y);
        if (_lastPlayerPos == pos)
            _stuckCounter++;
        else
            _stuckCounter = 0;
        _lastPlayerPos = pos;
    }


    private void ResetStuck(Entity player)
    {
        _stuckCounter  = 0;
        _lastPlayerPos = (player.X, player.Y);
    }

    // ── EmitDecision ──────────────────────────────────────────────────────────

    private static void EmitDecision(
        BotDecisionContext? context,
        Entity player,
        Fighter playerFighter,
        Inventory? inventory,
        List<Entity> aliveMonsters,
        string actionType,
        string reason,
        BotPersonaConfig persona)
    {
        if (context is null) return;

        var ctx = context.Value;
        double hpFraction = playerFighter.MaxHp > 0
            ? (double)playerFighter.Hp / playerFighter.MaxHp
            : 0.0;
        var adjacent = GetAdjacent(player, aliveMonsters);
        int potions  = CountHealingPotions(inventory);

        ctx.Recorder.Record(new BotDecisionRecord
        {
            TurnNumber              = ctx.TurnNumber,
            FloorDepth              = ctx.FloorDepth,
            Persona                 = ctx.Persona,
            ActionType              = actionType,
            Reason                  = reason,
            HpFraction              = hpFraction,
            VisibleEnemies          = aliveMonsters.Count,
            AdjacentEnemies         = adjacent.Count,
            HealingPotionsAvailable = potions,
            InCombat                = adjacent.Count > 0,
            LowHp                   = hpFraction <= persona.BaseHealThreshold,
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int CountHealingPotions(Inventory? inventory)
    {
        if (inventory == null) return 0;
        int count = 0;
        foreach (var item in inventory.Items)
        {
            var consumable = item.Get<Consumable>();
            if (consumable?.IsHealing == true)
                count += consumable.StackSize;
        }
        return count;
    }

    private static int CountWallNeighbors(int x, int y, GameMap map)
    {
        int count = 0;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                if (!map.IsWalkable(x + dx, y + dy)) count++;
            }
        return count;
    }

    private static bool AtChokePoint(Entity player, GameMap map)
        => CountWallNeighbors(player.X, player.Y, map) >= 3;

    private static (int x, int y)? FindChokePoint(Entity player, GameMap map, int searchRadius = 8)
    {
        (int x, int y)? best = null;
        int bestWalls = 1;
        double bestDist = double.MaxValue;

        for (int dx = -searchRadius; dx <= searchRadius; dx++)
        {
            for (int dy = -searchRadius; dy <= searchRadius; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = player.X + dx;
                int ny = player.Y + dy;
                if (!map.CanMoveTo(nx, ny)) continue;

                int walls = CountWallNeighbors(nx, ny, map);
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (walls > bestWalls || (walls == bestWalls && dist < bestDist))
                {
                    bestWalls = walls;
                    bestDist = dist;
                    best = (nx, ny);
                }
            }
        }

        return best;
    }

    private static Entity PickTarget(List<Entity> adjacent)
    {
        Entity best = adjacent[0];
        int bestHp = best.Require<Fighter>().Hp;

        for (int i = 1; i < adjacent.Count; i++)
        {
            int hp = adjacent[i].Require<Fighter>().Hp;
            if (hp < bestHp)
            {
                best = adjacent[i];
                bestHp = hp;
            }
        }
        return best;
    }

    private static List<Entity> GetAlive(List<Entity> monsters)
        => monsters.Where(m => m.Get<Fighter>()?.IsAlive == true).ToList();

    private static List<Entity> GetAdjacent(Entity player, List<Entity> alive)
        => alive.Where(m => player.ChebyshevDistanceTo(m.X, m.Y) <= 1).ToList();

    /// <summary>
    /// Pick an adjacent attack target, with hard-forced escalator priority for experiments.
    /// "escalator_first": always targets an Escalator/Fused monster if one is adjacent.
    /// "escalator_last": never targets Escalator/Fused while any non-escalator is adjacent.
    /// null/other: normal lowest-HP focus fire. Hard-forced keeps cohort membership clean.
    /// </summary>
    private static Entity PickTargetWithPriority(List<Entity> adjacent, BotPersonaConfig persona)
    {
        if (persona.EscalatorTargetingPriority == "escalator_first")
        {
            var escalator = adjacent.FirstOrDefault(IsEscalator);
            if (escalator != null) return escalator;
        }
        else if (persona.EscalatorTargetingPriority == "escalator_last")
        {
            var nonEscalators = adjacent.Where(m => !IsEscalator(m)).ToList();
            if (nonEscalators.Count > 0) return PickTarget(nonEscalators);
        }
        return PickTarget(adjacent);
    }

    private static Entity? FindNearestWithPriority(Entity from, List<Entity> candidates, BotPersonaConfig persona)
    {
        if (persona.EscalatorTargetingPriority == "escalator_first")
        {
            var nearestEscalator = FindNearest(from, candidates.Where(IsEscalator).ToList());
            if (nearestEscalator != null) return nearestEscalator;
        }
        else if (persona.EscalatorTargetingPriority == "escalator_last")
        {
            var nonEscalators = candidates.Where(m => !IsEscalator(m)).ToList();
            if (nonEscalators.Count > 0) return FindNearest(from, nonEscalators);
        }
        return FindNearest(from, candidates);
    }

    private static bool IsEscalator(Entity m)
        => m.Get<ThreatArchetypeTag>()?.Archetype is ThreatArchetype.Escalator or ThreatArchetype.Fused;

    /// <summary>
    /// Find the nearest floor item worth picking up within Chebyshev radius.
    /// "Worth picking up" = any consumable (potions, scrolls), wand (SpellEffect),
    /// equippable (weapon/armour), or key. Prioritises healing potions — they're returned
    /// first if one is within radius; otherwise falls back to any useful item.
    /// Returns null if nothing worth collecting is nearby.
    /// </summary>
    private static Entity? FindNearbyFloorItem(Entity player, IReadOnlyList<Entity> floorItems, int searchRadius)
    {
        Entity? bestHeal = null;
        Entity? bestOther = null;
        double bestHealDist = double.MaxValue;
        double bestOtherDist = double.MaxValue;

        foreach (var item in floorItems)
        {
            int dx = Math.Abs(item.X - player.X);
            int dy = Math.Abs(item.Y - player.Y);
            if (Math.Max(dx, dy) > searchRadius) continue;

            bool isUseful =
                item.Has<Consumable>()   // potions, scrolls
                || item.Has<SpellEffect>() // wands
                || item.Has<Equippable>(); // weapons, armour
            // Also grab keys (ItemTag with "key" in the type id)
            if (!isUseful)
            {
                var tag = item.Get<ItemTag>();
                if (tag != null && tag.TypeId.Contains("key", System.StringComparison.OrdinalIgnoreCase))
                    isUseful = true;
            }
            if (!isUseful) continue;

            double dist = player.DistanceTo(item.X, item.Y);
            var consumable = item.Get<Consumable>();
            if (consumable?.IsHealing == true)
            {
                if (dist < bestHealDist) { bestHealDist = dist; bestHeal = item; }
            }
            else
            {
                if (dist < bestOtherDist) { bestOtherDist = dist; bestOther = item; }
            }
        }

        // Prefer healing potions; fall back to any useful item
        return bestHeal ?? bestOther;
    }

    private static Entity? FindNearest(Entity from, List<Entity> candidates)
    {
        Entity? nearest = null;
        double bestDist = double.MaxValue;
        foreach (var c in candidates)
        {
            double d = from.DistanceTo(c.X, c.Y);
            if (d < bestDist)
            {
                bestDist = d;
                nearest = c;
            }
        }
        return nearest;
    }

    private static bool HasHealingPotion(Inventory? inventory)
    {
        if (inventory == null) return false;
        return inventory.FindFirst(item => item.Get<Consumable>()?.IsHealing == true) != null;
    }

    /// <summary>
    /// True when the player has a healing potion AND can currently use it (cooldown = 0 or item has no cooldown).
    /// Gate BotBrain's heal decisions on this, not bare HasHealingPotion, so a bot issuing Heal while on
    /// cooldown doesn't waste a turn producing a no-op.
    /// </summary>
    private static bool HasUsableHealingPotion(Inventory? inventory, Combat.Fighter fighter)
    {
        if (inventory == null) return false;
        // Shared availability answer — see QuickSlotModel. Was a private copy of the same
        // expression; the bot, the rules, and the bar now read one source.
        return inventory.FindFirst(item =>
            Content.QuickSlotModel.CanUseHealingPotion(item, fighter)) != null;
    }

    /// <summary>
    /// Convert a BotAction to a PlayerAction for TurnController consumption.
    /// AbortRun maps to Wait as a safe sentinel — harnesses intercept AbortRun
    /// BEFORE calling this method and never call ToPlayerAction for an abort.
    /// </summary>
    public static PlayerAction ToPlayerAction(BotAction action) => action.Type switch
    {
        BotAction.ActionType.AttackTarget => PlayerAction.Attack(action.Target!),
        BotAction.ActionType.HealSelf => PlayerAction.UseItem(),
        BotAction.ActionType.MoveToward => PlayerAction.MoveToward(action.Target!),
        BotAction.ActionType.MoveTo => PlayerAction.MoveTo(action.TargetX, action.TargetY),
        BotAction.ActionType.AbortRun => PlayerAction.Wait, // safe sentinel — harness intercepts first
        _ => PlayerAction.Wait,
    };
}

/// <summary>
/// Immutable bot decision — what action to take and on whom.
/// </summary>
public sealed class BotAction
{
    public enum ActionType
    {
        DoNothing,
        AttackTarget,
        HealSelf,
        MoveToward,
        MoveTo,
        /// <summary>
        /// Force descend — stuck counter hit the force-descend threshold (12 turns without moving).
        /// The harness navigates to the stair and descends even if the floor isn't clear.
        /// The run continues; the next floor gets a fresh attempt.
        /// Preferred over AbortRun because it preserves the run.
        /// </summary>
        ForceDescend,
        /// <summary>
        /// Abort the run — stuck counter exceeded the full abort threshold (20 turns).
        /// Only fires when even stair navigation has failed (e.g. stair unreachable).
        /// ToPlayerAction maps this to PlayerAction.Wait as a safe fallback (should never reach it).
        /// </summary>
        AbortRun,
    }

    public ActionType Type { get; }
    public Entity? Target { get; }
    public int TargetX { get; }
    public int TargetY { get; }

    private BotAction(ActionType type, Entity? target = null, int targetX = 0, int targetY = 0)
    {
        Type = type;
        Target = target;
        TargetX = targetX;
        TargetY = targetY;
    }

    public static BotAction None         => new(ActionType.DoNothing);
    public static BotAction Heal         => new(ActionType.HealSelf);
    public static BotAction ForceDescend => new(ActionType.ForceDescend);
    public static BotAction AbortRun     => new(ActionType.AbortRun);
    public static BotAction Attack(Entity target)   => new(ActionType.AttackTarget, target);
    public static BotAction Move(Entity toward)     => new(ActionType.MoveToward, toward);
    public static BotAction MoveTo(int x, int y)    => new(ActionType.MoveTo, targetX: x, targetY: y);
}
