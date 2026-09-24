using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;

namespace UnderWarden.Logic.AI;

/// <summary>
/// Standard monster AI: awareness tracking, A* pursuit, and melee combat.
///
/// Decision priority each turn:
///   1. Dead → Wait
///   2. Update awareness (FOV-based in dungeon mode, always-alerted in scenario mode)
///   3. Not alerted → Wait (idle)
///   4. Item seeking: if capable AND not actively fighting AND a floor item is closer than the player
///      → PickUp (on same tile) or SeekItem (A* toward item tile)
///   5. Item usage: 10% chance to use a held consumable (if AiComponent.CanUseItems = true)
///   6. Adjacent to player → Attack
///   7. Path exists → MoveTo first step
///   8. Path blocked → greedy MoveToward fallback, or Wait
/// </summary>
public static class BasicMonsterAI
{
    public static MonsterAction Decide(Entity monster, GameState state)
    {
        // 1. Dead monsters do nothing — guard against being called on a corpse.
        var fighter = monster.Get<Fighter>();
        if (fighter != null && !fighter.IsAlive)
            return MonsterAction.Wait();

        var player = state.Player;

        // 2. Awareness update.
        UpdateAwareness(monster, player, state);

        // 3. Not alerted → idle.
        var alertedState = monster.Get<AlertedState>();
        if (alertedState == null)
            return MonsterAction.Wait();

        // ── Phase 2/4: Status effect AI overrides ─────────────────────────────

        // FearEffect: monster flees from player. Does NOT attack, even when adjacent.
        // If no valid move away exists, stays in place. PoC-verified.
        if (monster.Has<FearEffect>())
            return DecideFlee(monster, player, state);

        // Resolve target: TauntedEffect forces targeting the player (overrides EnragedEffect).
        // EnragedEffect (HostileToAll) targets nearest entity regardless of allegiance.
        // Both are handled in ChooseTarget.
        var target = ChooseTarget(monster, player, state);

        // No-target sentinel (player-ally with no hostile in range): nothing to fight, idle.
        if (target.Id == monster.Id)
            return MonsterAction.Wait();

        bool targetAdjacent = monster.ChebyshevDistanceTo(target.X, target.Y) <= 1;

        // DisorientationEffect: monster moves in a random direction instead of pursuing.
        // Attacks are NOT affected — if already adjacent, monster still attacks.
        // Movement randomization applies to the pursuit path only (PoC-verified).
        if (monster.Has<DisorientationEffect>())
        {
            if (targetAdjacent && !target.Has<InvisibilityEffect>())
                return MonsterAction.Attack(target);
            return DecideRandomMove(monster, state);
        }

        // EntangledEffect: monster cannot move but CAN attack adjacent targets.
        if (monster.Has<EntangledEffect>())
        {
            if (targetAdjacent && !target.Has<InvisibilityEffect>())
                return MonsterAction.Attack(target);
            return MonsterAction.Wait(); // rooted, cannot pursue
        }

        // ImmobilizedEffect: ProcessTurnStart already returns skipTurn=true for the full-action block.
        // We don't need to intercept here — TurnController skips the entire action.
        // This branch is kept as documentation clarity but should never fire in practice.

        // ── Normal AI flow ─────────────────────────────────────────────────────

        // 4. Item seeking — only when not actively in melee contact with the chosen target.
        if (!targetAdjacent)
        {
            var itemAction = TryItemSeek(monster, state);
            if (itemAction != null)
                return itemAction;
        }

        // 5. Item usage: 10% chance per turn to use a held consumable.
        // Gated behind AiComponent.CanUseItems — false by default to match PoC's
        // can_use_potions = False. Enable per-monster in YAML once the scroll system
        // lands and balance is validated.
        // Fires before the attack check so a monster might heal instead of swinging.
        var ai = monster.Get<AiComponent>();
        if (ai != null && ai.CanUseItems)
        {
            var monsInventory = monster.Get<Inventory>();
            if (monsInventory != null && monsInventory.Count > 0 && state.Rng.Next(0, 100) < 10)
            {
                var usable = monsInventory.FindFirst(item => item.Get<Consumable>()?.IsHealing == true);
                if (usable != null)
                    return MonsterAction.UseItem(usable);
            }
        }

        // 6. Adjacent to target → attack immediately.
        // Chebyshev distance 1 = any of the 8 surrounding tiles (diagonal melee included).
        // Invisible entities cannot be targeted for direct attacks — the AI can't see them.
        // They can still be damaged by AoE. InvisibilityEffect breaks on the player's own attack.
        if (targetAdjacent && !target.Has<InvisibilityEffect>())
            return MonsterAction.Attack(target);

        // If adjacent but target is invisible: fall through to movement logic.
        // Monster will wait or pursue a remembered position instead of attacking.

        // 7. Pursue toward target using A*.
        // When target is the player, use last-known position (handles invisibility/fog).
        // When target is a monster (faction hostility), pursue directly toward it.
        // Pass the monster as movingEntity so the pathfinder doesn't treat it as self-blocking.
        bool targetIsPlayer = target.Id == state.Player.Id;
        int pursueX = targetIsPlayer ? alertedState.LastKnownPlayerX : target.X;
        int pursueY = targetIsPlayer ? alertedState.LastKnownPlayerY : target.Y;

        bool canOpenDoors = monster.Get<Fighter>()?.CanOpenDoors ?? false;
        var path = Pathfinder.AStar(
            state.Map,
            monster.X, monster.Y,
            pursueX, pursueY,
            movingEntity: monster,
            canPassDoors: canOpenDoors);

        if (path != null && path.Count > 0)
            return MonsterAction.MoveTo(path[0].X, path[0].Y);

        // 8. A* failed (unreachable or no valid path) — try greedy as a last resort.
        // MoveToward mutates the entity's position directly, so we only call it if
        // a greedy step actually exists. We detect this by checking if the target differs
        // from the monster's current position (otherwise MoveToward is a no-op anyway).
        if (monster.X != pursueX || monster.Y != pursueY)
        {
            // Greedy step: attempt to move one tile closer, return MoveTo if a direction is free.
            // We compute the intended greedy destination rather than mutating position here —
            // let TurnController apply the move to stay consistent with the action model.
            int dx = Math.Sign(pursueX - monster.X);
            int dy = Math.Sign(pursueY - monster.Y);

            // Try diagonal, then axis-aligned — mirrors GameMap.MoveToward's priority order.
            if (dx != 0 && dy != 0 && state.Map.CanMoveTo(monster.X + dx, monster.Y + dy))
                return MonsterAction.MoveTo(monster.X + dx, monster.Y + dy);
            if (dx != 0 && state.Map.CanMoveTo(monster.X + dx, monster.Y))
                return MonsterAction.MoveTo(monster.X + dx, monster.Y);
            if (dy != 0 && state.Map.CanMoveTo(monster.X, monster.Y + dy))
                return MonsterAction.MoveTo(monster.X, monster.Y + dy);
        }

        // Fully blocked or already at the target — stand still.
        return MonsterAction.Wait();
    }

    /// <summary>
    /// Updates the monster's AlertedState based on the current game mode.
    ///
    /// Three modes:
    ///   Dungeon mode (IsDungeonMode=true): FOV-based — alert when visible, deaggro when unseen.
    ///   Harness mode (IsHarnessMode=true):  PoC-matching — passive until attacked. Replicates
    ///     the PoC's scenario_harness.py behavior where fov_map=None makes monster_sees_player
    ///     always False, so monsters only act when in_combat=True (set on first hit).
    ///     We proxy "was attacked" as Hp &lt; MaxHp. Once alerted, the monster stays active.
    ///   Scenario mode (neither):            Always-alerted — all monsters active from turn 1.
    ///     Used by unit tests that need monsters acting immediately.
    /// </summary>
    internal static void UpdateAwareness(Entity monster, Entity player, GameState state)
    {
        if (state.IsDungeonMode)
        {
            // Dungeon: per-turn FOV. Alert when visible, deaggro after DeaggroTurns unseen.
            if (state.Map.IsVisible(monster.X, monster.Y))
            {
                var alert = monster.GetOrAdd<AlertedState>();
                alert.LastKnownPlayerX = player.X;
                alert.LastKnownPlayerY = player.Y;
                alert.TurnsUntilDeaggro = AlertedState.DeaggroTurns;
            }
            else
            {
                var alert = monster.Get<AlertedState>();
                if (alert != null)
                {
                    alert.TurnsUntilDeaggro--;
                    if (alert.TurnsUntilDeaggro <= 0)
                        monster.Remove<AlertedState>();
                }
            }
        }
        else if (state.IsHarnessMode)
        {
            // Harness: PoC-matching passive-until-attacked. A monster activates only after
            // it takes damage. Once alerted it stays alerted for the run.
            //
            // We compare Hp against BaseMaxHp (not MaxHp) because Hp is initialized to
            // BaseMaxHp while MaxHp includes ConstitutionMod — monsters with CON > 10 would
            // always read Hp < MaxHp on turn 1, firing the proxy incorrectly.
            var fighter = monster.Get<Fighter>();
            bool hasBeenAttacked = fighter != null && fighter.IsAlive && fighter.Hp < fighter.BaseMaxHp;
            bool alreadyAlerted  = monster.Has<AlertedState>();

            if (hasBeenAttacked || alreadyAlerted)
            {
                var alert = monster.GetOrAdd<AlertedState>();
                alert.LastKnownPlayerX = player.X;
                alert.LastKnownPlayerY = player.Y;
                alert.TurnsUntilDeaggro = AlertedState.DeaggroTurns;
            }
            // Otherwise monster stays passive — no AlertedState → returns Wait in Decide().
        }
        else
        {
            // Regular scenario mode (unit tests): all monsters permanently alerted from turn 1.
            var alert = monster.GetOrAdd<AlertedState>();
            alert.LastKnownPlayerX = player.X;
            alert.LastKnownPlayerY = player.Y;
            alert.TurnsUntilDeaggro = AlertedState.DeaggroTurns;
        }
    }

    /// <summary>
    /// Choose the attack target for this monster.
    ///
    /// Priority:
    ///   1. TauntedEffect: always target the taunted entity's TauntTargetId (player).
    ///   2. EnragedEffect (HostileToAll): find nearest alive entity — player or any monster.
    ///   3. Faction hostility: find nearest hostile entity (player or monster). Player preferred on tie.
    ///   4. Fallback: target the player.
    ///
    /// Returns player if no special targeting applies or if no hostile entity is found.
    /// </summary>
    public static Entity ChooseTarget(Entity monster, Entity player, GameState state)
    {
        // Taunt overrides everything — even EnragedEffect and faction targeting.
        var taunt = monster.Get<TauntedEffect>();
        if (taunt != null)
        {
            // TauntTargetId -1 means unset; default to player.
            if (taunt.TauntTargetId == player.Id || taunt.TauntTargetId < 0)
                return player;
            // Future: look up the entity by ID if we have non-player taunt targets.
            return player;
        }

        // EnragedEffect: HostileToAll flag — find nearest entity regardless of faction.
        if (monster.Has<EnragedEffect>())
        {
            Entity? nearest = null;
            int nearestDist = int.MaxValue;

            int playerDist = monster.ChebyshevDistanceTo(player.X, player.Y);
            if (playerDist < nearestDist)
            {
                nearest = player;
                nearestDist = playerDist;
            }

            foreach (var other in state.AliveMonsters)
            {
                if (other.Id == monster.Id) continue;
                int d = monster.ChebyshevDistanceTo(other.X, other.Y);
                if (d < nearestDist)
                {
                    nearest = other;
                    nearestDist = d;
                }
            }

            return nearest ?? player;
        }

        // Faction-aware targeting: find nearest hostile entity.
        // Player is always hostile and gets highest priority (10).
        // Monsters of hostile factions are secondary targets.
        string myFaction = monster.Get<AiComponent>()?.Faction ?? "neutral";
        bool playerInvisible = player.Has<InvisibilityEffect>();

        Entity? bestTarget = null;
        int bestDist = int.MaxValue;
        int bestPriority = -1;

        // Consider player — unless invisible, or unless the chooser is on Sasha's side
        // (a player-ally is not hostile to the player). TASK-005: gate the previously-hardcoded
        // "always target the player" branch behind faction hostility so allies don't attack Sasha.
        if (!playerInvisible && FactionRegistry.AreHostile(myFaction, FactionRegistry.PlayerFaction))
        {
            int d = monster.ChebyshevDistanceTo(player.X, player.Y);
            bestTarget = player;
            bestDist = d;
            bestPriority = FactionRegistry.GetTargetPriority(myFaction, "player"); // 10
        }

        // Consider hostile monsters
        foreach (var other in state.AliveMonsters)
        {
            if (other.Id == monster.Id) continue;
            string otherFaction = other.Get<AiComponent>()?.Faction ?? "neutral";
            if (!FactionRegistry.AreHostile(myFaction, otherFaction)) continue;

            int d = monster.ChebyshevDistanceTo(other.X, other.Y);
            int priority = FactionRegistry.GetTargetPriority(myFaction, otherFaction);

            // Prefer higher priority, then closer distance
            if (priority > bestPriority || (priority == bestPriority && d < bestDist))
            {
                bestTarget = other;
                bestDist = d;
                bestPriority = priority;
            }
        }

        // No hostile found. Monsters fall back to the player (their universal enemy). A player-ally
        // has no enemy to fall back to, so it returns itself as a no-target sentinel — Decide reads
        // that as "idle this turn".
        if (bestTarget != null) return bestTarget;
        return FactionRegistry.AreHostile(myFaction, FactionRegistry.PlayerFaction) ? player : monster;
    }

    /// <summary>
    /// Flee AI: move to maximize distance from the player.
    /// If all adjacent passable tiles are blocked or none increases distance, stay in place.
    /// Never attacks while feared (PoC-verified).
    /// </summary>
    internal static MonsterAction DecideFlee(Entity monster, Entity player, GameState state)
    {
        int bestX = monster.X, bestY = monster.Y;
        // Use Manhattan consistently: both the baseline and candidate evaluations use the same metric.
        int bestDist = Math.Abs(monster.X - player.X) + Math.Abs(monster.Y - player.Y);

        // Try all 8 adjacent tiles; pick the one that maximizes distance from player.
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = monster.X + dx;
                int ny = monster.Y + dy;
                if (!state.Map.CanMoveTo(nx, ny)) continue;

                int dist = Math.Abs(nx - player.X) + Math.Abs(ny - player.Y);
                if (dist > bestDist)
                {
                    bestDist = dist;
                    bestX = nx;
                    bestY = ny;
                }
            }
        }

        // If no better tile found (cornered), stay in place.
        if (bestX == monster.X && bestY == monster.Y)
            return MonsterAction.Wait();

        return MonsterAction.MoveTo(bestX, bestY);
    }

    /// <summary>
    /// Random movement for DisorientationEffect: pick a random adjacent passable tile.
    /// If the chosen direction hits a wall, no movement this turn (PoC-verified).
    /// </summary>
    internal static MonsterAction DecideRandomMove(Entity monster, GameState state)
    {
        // All 8 adjacent directions
        var dirs = new (int dx, int dy)[]
        {
            (-1, -1), (0, -1), (1, -1),
            (-1,  0),          (1,  0),
            (-1,  1), (0,  1), (1,  1),
        };

        // Shuffle using Fisher-Yates with the game's RNG to stay deterministic.
        var shuffled = dirs.ToList();
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = state.Rng.Next(0, i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        foreach (var (dx, dy) in shuffled)
        {
            int nx = monster.X + dx;
            int ny = monster.Y + dy;
            if (state.Map.CanMoveTo(nx, ny))
                return MonsterAction.MoveTo(nx, ny);
        }

        // All directions blocked — no move.
        return MonsterAction.Wait();
    }

    /// <summary>
    /// Check whether this monster should seek a floor item this turn.
    /// Returns a PickUp or SeekItem action if an eligible item exists, null otherwise.
    ///
    /// Eligibility rules (mirror the PoC's ItemSeekingAI):
    ///   - Monster has AiComponent.CanSeekItems = true
    ///   - Monster has an Inventory that isn't full (respecting AiComponent.InventorySize)
    ///   - At least one floor item within AiComponent.SeekDistance (Chebyshev)
    ///   - That item is closer to the monster than the player is (prevents abandoning pursuit
    ///     of a player to run across the map for gear)
    /// </summary>
    private static MonsterAction? TryItemSeek(Entity monster, GameState state)
    {
        var ai = monster.Get<AiComponent>();
        if (ai == null || !ai.CanSeekItems) return null;

        var inventory = monster.Get<Inventory>();
        if (inventory == null) return null;

        // Respect the per-monster inventory cap defined in AiComponent.InventorySize.
        // Equipment.MainHand/Chest count separately — the inventory cap covers carried (un-equipped) items.
        // Keep it simple: if AiComponent.InventorySize == 0 means unlimited (fall back to Inventory.Capacity).
        int effectiveCap = ai.InventorySize > 0 ? ai.InventorySize : Inventory.Capacity;
        if (inventory.Count >= effectiveCap) return null;

        var player = state.Player;
        int playerDist = monster.ChebyshevDistanceTo(player.X, player.Y);

        Entity? bestItem = null;
        int bestDist = int.MaxValue;

        foreach (var item in state.FloorItems)
        {
            int dist = monster.ChebyshevDistanceTo(item.X, item.Y);
            if (dist > ai.SeekDistance) continue;          // out of detection range
            if (dist >= playerDist) continue;              // player is closer or equal — fight first
            if (dist < bestDist)
            {
                bestItem = item;
                bestDist = dist;
            }
        }

        if (bestItem == null) return null;

        // On the same tile → pick up immediately.
        if (bestDist == 0)
            return MonsterAction.PickUp(bestItem);

        // Navigate toward the item using A* for the first step, just like player pursuit.
        bool canOpenDoorsForItem = monster.Get<Fighter>()?.CanOpenDoors ?? false;
        var path = Pathfinder.AStar(
            state.Map,
            monster.X, monster.Y,
            bestItem.X, bestItem.Y,
            movingEntity: monster,
            canPassDoors: canOpenDoorsForItem);

        if (path != null && path.Count > 0)
            return MonsterAction.SeekItem(path[0].X, path[0].Y);

        // A* blocked — skip seeking this turn rather than deadlocking.
        return null;
    }
}
