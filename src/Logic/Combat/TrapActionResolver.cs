using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

/// <summary>
/// Resolves a TrapPayloadComponent against a target entity.
/// Trigger-agnostic: called by both walk-over (floor traps) and bump-to-interact (props, trapped chests).
///
/// Action ordering within a payload:
///   1. Status effects (bleed, acid, burning, poison, slow, entangle) — applied first so
///      any damage-reaction logic can see the status when damage fires.
///   2. Direct damage — after statuses are applied.
///   3. World-changing actions (teleport, alert_faction, descend, spawn_monster) — last,
///      so the creature's fate is known before repositioning occurs.
///
/// Thread safety: not applicable — GameState is single-threaded.
/// </summary>
public static class TrapActionResolver
{
    /// <summary>
    /// Resolve all actions in the payload against the target entity.
    /// Returns true if any action was resolved (for telemetry/event emission by caller).
    ///
    /// Parameters:
    ///   target          — the entity affected (player or monster)
    ///   payload         — the trap actions to apply
    ///   source          — label for TrapTriggeredEvent (trap type ID or prop name)
    ///   originTile      — (X, Y) tile where the trap/prop is located
    ///   state           — full game state (needed for teleport, faction alert, spawn)
    ///   rng             — seeded RNG for teleport destination, spawn position
    ///   events          — accumulates TurnEvents
    ///   monsterFactory  — required for spawn_monster action; null → no-op
    ///   idAllocator     — required for spawn_monster action; null → no-op
    /// </summary>
    public static bool Resolve(
        Entity target,
        TrapPayloadComponent payload,
        string source,
        (int X, int Y) originTile,
        GameState state,
        SeededRandom rng,
        List<TurnEvent> events,
        MonsterFactory? monsterFactory = null,
        EntityIdAllocator? idAllocator = null)
    {
        if (payload.Actions.Count == 0) return false;

        var fighter = target.Get<Fighter>();
        bool anyResolved = false;

        // ─── Pass 1: status effects (applied before damage) ────────────────────
        foreach (var action in payload.Actions)
        {
            bool resolved = action.Kind switch
            {
                "burning"  => ApplyBurning(target, action, events),
                "poison"   => ApplyPoison(target, action, events),
                "slow"     => ApplySlow(target, action, events),
                "entangle" => ApplyEntangle(target, action, events),
                "bleed"    => ApplyBleed(target, action, events),
                "acid"     => ApplyAcid(target, action, events),
                _          => false,
            };
            if (resolved) anyResolved = true;
        }

        // ─── Pass 2: damage ────────────────────────────────────────────────────
        foreach (var action in payload.Actions)
        {
            if (action.Kind != "damage") continue;
            if (fighter == null) continue;

            fighter.TakeDamage(action.Amount);
            anyResolved = true;

            if (!fighter.IsAlive)
            {
                events.Add(new DeathEvent { ActorId = target.Id, KillerId = -1 });
                // Do not continue resolving world-changing actions against a dead entity.
                EmitTriggerEvent(target, source, originTile, payload, events);
                return true;
            }
        }

        // ─── Pass 3: world-changing actions ────────────────────────────────────
        foreach (var action in payload.Actions)
        {
            bool resolved = action.Kind switch
            {
                "teleport"      => ApplyTeleport(target, originTile, state, rng, events),
                "alert_faction" => ApplyAlertFaction(action, originTile, state, events),
                "descend"       => ApplyDescend(target, state, events),
                "spawn_monster" => ApplySpawnMonster(action, originTile, state, rng, events, monsterFactory, idAllocator),
                _               => false,
            };
            if (resolved) anyResolved = true;
        }

        if (anyResolved)
            EmitTriggerEvent(target, source, originTile, payload, events);

        return anyResolved;
    }

    // ─── Status effect helpers ────────────────────────────────────────────────

    private static bool ApplyBurning(Entity target, TrapAction action, List<TurnEvent> events)
    {
        int duration = action.Duration > 0 ? action.Duration : 5;
        var effect = StatusEffectProcessor.ApplyEffect<BurningEffect>(target, duration);
        if (effect == null) return false;

        events.Add(new StatusAppliedEvent
        {
            ActorId   = target.Id,
            TargetId  = target.Id,
            EffectName = "burning",
            Duration  = effect.RemainingTurns,
        });
        return true;
    }

    private static bool ApplyPoison(Entity target, TrapAction action, List<TurnEvent> events)
    {
        int duration = action.Duration > 0 ? action.Duration : 6;
        var effect = StatusEffectProcessor.ApplyEffect<PoisonEffect>(target, duration);
        if (effect == null) return false;

        events.Add(new StatusAppliedEvent
        {
            ActorId   = target.Id,
            TargetId  = target.Id,
            EffectName = "poison",
            Duration  = effect.RemainingTurns,
        });
        return true;
    }

    private static bool ApplySlow(Entity target, TrapAction action, List<TurnEvent> events)
    {
        int duration = action.Duration > 0 ? action.Duration : 5;
        var effect = StatusEffectProcessor.ApplyEffect<SlowedEffect>(target, duration);
        if (effect == null) return false;

        events.Add(new StatusAppliedEvent
        {
            ActorId   = target.Id,
            TargetId  = target.Id,
            EffectName = "slowed",
            Duration  = effect.RemainingTurns,
        });
        return true;
    }

    private static bool ApplyEntangle(Entity target, TrapAction action, List<TurnEvent> events)
    {
        int duration = action.Duration > 0 ? action.Duration : 3;
        var effect = StatusEffectProcessor.ApplyEffect<EntangledEffect>(target, duration);
        if (effect == null) return false;

        events.Add(new StatusAppliedEvent
        {
            ActorId   = target.Id,
            TargetId  = target.Id,
            EffectName = "entangled",
            Duration  = effect.RemainingTurns,
        });
        return true;
    }

    /// <summary>
    /// Apply bleed effect (Phase 6 full implementation).
    /// Severity comes from action.Amount (1 = standard, 2 = deep wound).
    /// Duration from action.Duration (default 3 if unset).
    /// BleedEffect ticks damage each turn and attracts nearby undead.
    /// </summary>
    private static bool ApplyBleed(Entity target, TrapAction action, List<TurnEvent> events)
    {
        int duration = action.Duration > 0 ? action.Duration : 3;
        int severity = action.Amount > 0 ? action.Amount : 1;

        var effect = StatusEffectProcessor.ApplyEffect<BleedEffect>(target, duration);
        if (effect == null) return false;

        // Set severity — take the higher value if already bleeding (no downgrade).
        effect.Severity = Math.Max(effect.Severity, severity);

        events.Add(new StatusAppliedEvent
        {
            ActorId    = target.Id,
            TargetId   = target.Id,
            EffectName = "bleed",
            Duration   = effect.RemainingTurns,
        });
        return true;
    }

    /// <summary>
    /// Apply acid effect (Phase 6 full implementation).
    /// Duration from action.Duration (default 8 if unset, matching acid_trap YAML).
    /// While active: suppresses InnateRegenComponent on the same entity.
    /// </summary>
    private static bool ApplyAcid(Entity target, TrapAction action, List<TurnEvent> events)
    {
        int duration = action.Duration > 0 ? action.Duration : 8;

        var effect = StatusEffectProcessor.ApplyEffect<AcidEffect>(target, duration);
        if (effect == null) return false;

        events.Add(new StatusAppliedEvent
        {
            ActorId    = target.Id,
            TargetId   = target.Id,
            EffectName = "acid",
            Duration   = effect.RemainingTurns,
        });
        return true;
    }

    // ─── World-changing action helpers ────────────────────────────────────────

    private static bool ApplyTeleport(Entity target, (int X, int Y) originTile,
        GameState state, SeededRandom rng, List<TurnEvent> events)
    {
        var dest = FindRandomWalkableTile(state, rng, excludeX: target.X, excludeY: target.Y);
        if (dest == null) return false;

        int fromX = target.X, fromY = target.Y;
        target.X = dest.Value.X;
        target.Y = dest.Value.Y;

        events.Add(new TeleportEvent
        {
            ActorId = target.Id,
            EntityId = target.Id,
            FromX = fromX,
            FromY = fromY,
            ToX   = dest.Value.X,
            ToY   = dest.Value.Y,
            Reason = "trap",
        });
        return true;
    }

    private static bool ApplyAlertFaction(TrapAction action, (int X, int Y) originTile,
        GameState state, List<TurnEvent> events)
    {
        string faction = action.Target;
        int radius = action.Radius > 0 ? action.Radius : 8;
        bool anyAlerted = false;

        // Find all alive faction monsters within radius of the trap's origin tile.
        // Sort by ID for determinism — no dictionary enumeration in loops.
        var candidates = state.AliveMonsters
            .Where(m =>
            {
                var ai = m.Get<AiComponent>();
                if (ai == null || ai.Faction != faction) return false;
                int dx = m.X - originTile.X;
                int dy = m.Y - originTile.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                return dist <= radius;
            })
            .OrderBy(m => m.Id) // deterministic ordering
            .ToList();

        foreach (var monster in candidates)
        {
            // Only alert monsters that are not already alerted toward the player.
            if (monster.Has<AlertedState>()) continue;

            monster.Add(new AlertedState
            {
                LastKnownPlayerX = state.Player.X,
                LastKnownPlayerY = state.Player.Y,
                TurnsUntilDeaggro = AlertedState.DeaggroTurns,
            });
            anyAlerted = true;
        }

        return anyAlerted;
    }

    private static bool ApplyDescend(Entity target, GameState state, List<TurnEvent> events)
    {
        // Only meaningful for the player — monsters cannot descend.
        if (target.Id != state.Player.Id) return false;

        events.Add(new DescendEvent
        {
            ActorId  = target.Id,
            NewDepth = state.CurrentDepth + 1,
            Cause    = "hole_trap",
        });
        return true;
    }

    private static bool ApplySpawnMonster(TrapAction action, (int X, int Y) originTile,
        GameState state, SeededRandom rng, List<TurnEvent> events,
        MonsterFactory? monsterFactory, EntityIdAllocator? idAllocator)
    {
        if (monsterFactory == null || idAllocator == null) return false;
        if (string.IsNullOrEmpty(action.Target)) return false;

        int radius = action.Radius > 0 ? action.Radius : 4;
        var spawnPos = FindFreePositionInRadius(state, rng, originTile.X, originTile.Y, radius);
        if (spawnPos == null) return false;

        // Spawn the monster using depth-appropriate stats.
        var entity = monsterFactory.Create(action.Target, spawnPos.Value.X, spawnPos.Value.Y,
            state.CurrentDepth, rng);
        if (entity == null) return false;

        // Re-wrap with an allocator-managed ID to avoid collision with existing entities.
        var withId = new Entity(idAllocator.Next(), entity.Name, spawnPos.Value.X, spawnPos.Value.Y,
            entity.BlocksMovement);
        foreach (var comp in entity.GetAllComponents())
            withId.Add(comp);

        state.Monsters.Add(withId);
        state.Map.RegisterEntity(withId);

        // NOTE: spawned monsters naturally skip the current turn because ResolveMonsterTurns
        // snapshots AliveMonsters at the top of the method. Monsters added mid-turn are
        // not in the snapshot and will first act on the following turn (PoC-correct behavior).

        events.Add(new MonsterRousedEvent
        {
            ActorId         = withId.Id,
            SpawnedEntityId = withId.Id,
            MonsterType     = action.Target,
            OriginX         = originTile.X,
            OriginY         = originTile.Y,
        });

        return true;
    }

    // ─── Utility helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Find a random walkable, unblocked tile anywhere on the map.
    /// Excludes the origin tile (excludeX, excludeY) to prevent teleporting in place.
    /// Returns null if no valid tile found after sampling.
    /// </summary>
    private static (int X, int Y)? FindRandomWalkableTile(GameState state, SeededRandom rng,
        int excludeX, int excludeY)
    {
        var map = state.Map;
        var candidates = new List<(int X, int Y)>();

        for (int x = 0; x < map.Width; x++)
        for (int y = 0; y < map.Height; y++)
        {
            if (x == excludeX && y == excludeY) continue;
            if (map.CanMoveTo(x, y))
                candidates.Add((x, y));
        }

        if (candidates.Count == 0) return null;
        return candidates[rng.Next(candidates.Count)];
    }

    /// <summary>
    /// Find a free (walkable + unblocked) tile within Chebyshev radius of (cx, cy).
    /// Used for spawn_monster placement. Returns null when no tile is free (no-op rouse).
    /// Uses sorted tile order for determinism.
    /// </summary>
    private static (int X, int Y)? FindFreePositionInRadius(GameState state, SeededRandom rng,
        int cx, int cy, int radius)
    {
        var map = state.Map;
        var candidates = new List<(int X, int Y)>();

        // Collect in sorted order (row-major x,y) for determinism — no dict enumeration.
        for (int x = cx - radius; x <= cx + radius; x++)
        for (int y = cy - radius; y <= cy + radius; y++)
        {
            if (!map.InBounds(x, y)) continue;
            if (x == cx && y == cy) continue; // not on the origin tile itself
            if (map.CanMoveTo(x, y))
                candidates.Add((x, y));
        }

        if (candidates.Count == 0) return null;
        return candidates[rng.Next(candidates.Count)];
    }

    /// <summary>
    /// Emit a TrapTriggeredEvent listing all action kinds from the payload.
    /// Emitted once per Resolve call (not once per action).
    /// </summary>
    private static void EmitTriggerEvent(Entity target, string source, (int X, int Y) originTile,
        TrapPayloadComponent payload, List<TurnEvent> events)
    {
        var kinds = payload.Actions.Select(a => a.Kind).ToArray();
        events.Add(new TrapTriggeredEvent
        {
            ActorId     = target.Id,
            TargetId    = target.Id,
            X           = originTile.X,
            Y           = originTile.Y,
            Source      = source,
            ActionKinds = kinds,
        });
    }
}
