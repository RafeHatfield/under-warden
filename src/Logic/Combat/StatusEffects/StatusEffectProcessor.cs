using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.AI;

namespace UnderWarden.Logic.Combat.StatusEffects;

/// <summary>
/// Processes status effects for a single entity each turn.
/// Stateless — all state lives in the entity's components.
///
/// Turn lifecycle:
///   ProcessTurnStart → DOT ticks, HOT ticks, skip-turn checks (returns true if turn skipped)
///   ProcessTurnEnd   → decrement durations, remove expired effects, emit StatusExpiredEvent
///
/// Wire points:
///   TurnController.ResolveMonsterTurns / ResolvePlayerAction — call both methods per entity
///   CombatResolver.ResolveAttack — call OnDamageTaken on attack hits to wake Sleep
///   DungeonFloorBuilder.Build (carry-forward path) — call ClearAllEffects on player
/// </summary>
public static class StatusEffectProcessor
{
    // Maps C# effect types to YAML immunity key strings.
    // StatusImmunityComponent stores YAML keys ("confusion", "slow", etc.)
    // and this dictionary bridges the generic type to the key for lookup.
    private static readonly Dictionary<Type, string> EffectImmunityKeys = new()
    {
        [typeof(DisorientationEffect)] = "confusion",
        [typeof(SlowedEffect)] = "slow",
        [typeof(FearEffect)] = "fear",
        [typeof(PoisonEffect)] = "poison",
        [typeof(PlagueEffect)] = "plague",
        [typeof(BurningEffect)] = "burning",
        [typeof(ImmobilizedEffect)] = "immobilized",
        [typeof(SleepEffect)] = "sleep",
        [typeof(BlindedEffect)] = "blinded",
        [typeof(SilencedEffect)] = "silenced",
        [typeof(WeaknessEffect)] = "weakness",
        [typeof(BleedEffect)] = "bleed",
        [typeof(AcidEffect)] = "acid",
        [typeof(PossessionEffect)] = "possessed",
        [typeof(EngulfedEffect)] = "engulfed",
        [typeof(DissonantChantEffect)] = "dissonant_chant",
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Apply
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Apply an effect to an entity using the no-stack/refresh rule.
    /// If the entity already has this effect, refreshes duration to the maximum of
    /// the current remaining turns and the requested duration (no downgrade).
    /// If the entity does not have this effect, creates and attaches a new instance.
    /// Returns the (possibly existing) effect component, or null if blocked by immunity or FreeActionTag.
    ///
    /// StatusImmunityComponent: entities with this component are immune to specific effects
    /// (e.g. wraith immune to confusion/slow/fear). Checked before FreeActionTag.
    ///
    /// FreeActionTag: entities with this tag are immune to SlowedEffect and ImmobilizedEffect.
    /// Other effects (poison, burning, sleep, etc.) are not blocked.
    /// </summary>
    public static T? ApplyEffect<T>(Entity entity, int duration) where T : class, IStatusEffect, new()
    {
        // Status immunity check: wraith, lich, etc. have defined immunities from YAML.
        var immunities = entity.Get<StatusImmunityComponent>();
        if (immunities != null && EffectImmunityKeys.TryGetValue(typeof(T), out var key) && immunities.IsImmuneTo(key))
            return null;

        // FreeAction blocks slow and paralysis application entirely.
        // The tag check is on the concrete type to keep this O(1) and avoid a type string comparison.
        if (entity.Has<FreeActionTag>())
        {
            if (typeof(T) == typeof(SlowedEffect) || typeof(T) == typeof(ImmobilizedEffect))
                return null;
        }

        var existing = entity.Get<T>();
        if (existing != null)
        {
            // No-stack: take the longer of the two durations
            existing.RemainingTurns = Math.Max(existing.RemainingTurns, duration);
            return existing;
        }
        var effect = new T { RemainingTurns = duration };
        entity.Add(effect);
        return effect;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Turn Start (DOT, HOT, skip-turn checks)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Process start-of-turn effects: DOT damage, HOT healing, and skip-turn determination.
    /// Returns true if the entity should skip its action this turn.
    ///
    /// SlowedEffect skip rule: odd-numbered turns are skipped (turnCount % 2 == 1).
    /// This matches PoC behavior where the slow effect gates every other turn.
    ///
    /// state: optional GameState for bleed attraction (undead alert when player bleeds).
    /// Null in unit tests that don't need the full state.
    /// </summary>
    public static bool ProcessTurnStart(Entity entity, List<TurnEvent> events, int turnCount = 0,
        GameState? state = null)
    {
        var fighter = entity.Get<Fighter>();
        bool skipTurn = false;

        // ── DOT ticks ──────────────────────────────────────────────────────

        if (entity.Get<PoisonEffect>() is { } poison && fighter != null)
        {
            fighter.TakeDamage(poison.DamagePerTurn);
            events.Add(new DotDamageEvent
            {
                ActorId = entity.Id,
                EntityId = entity.Id,
                EffectName = "poison",
                Damage = poison.DamagePerTurn,
            });
        }

        if (entity.Get<BurningEffect>() is { } burning && fighter != null)
        {
            fighter.TakeDamage(burning.DamagePerTurn);
            events.Add(new DotDamageEvent
            {
                ActorId = entity.Id,
                EntityId = entity.Id,
                EffectName = "burning",
                Damage = burning.DamagePerTurn,
            });
        }

        if (entity.Get<PlagueEffect>() is { } plague && fighter != null)
        {
            fighter.TakeDamage(plague.DamagePerTurn);
            events.Add(new DotDamageEvent
            {
                ActorId = entity.Id,
                EntityId = entity.Id,
                EffectName = "plague",
                Damage = plague.DamagePerTurn,
            });
        }

        // BleedEffect: DOT damage + undead attraction.
        if (entity.Get<BleedEffect>() is { } bleed && fighter != null)
        {
            int bleedDmg = bleed.DamagePerTick;
            fighter.TakeDamage(bleedDmg);
            events.Add(new BleedTickEvent
            {
                ActorId = entity.Id,
                Damage  = bleedDmg,
            });

            // Undead attraction: alert nearby undead toward the bleeding entity.
            // Cap: BleedAttractionCapPerTick per tick to prevent surging.
            if (state != null)
                ApplyBleedAttraction(entity, state, events);
        }

        // ── HOT ticks ──────────────────────────────────────────────────────

        if (entity.Get<RegenerationEffect>() is { } regen && fighter != null)
        {
            int actualHeal = fighter.Heal(regen.HealPerTurn);
            if (actualHeal > 0)
            {
                events.Add(new HotHealEvent
                {
                    ActorId = entity.Id,
                    EntityId = entity.Id,
                    EffectName = "regeneration",
                    Amount = actualHeal,
                });
            }
        }

        // InnateRegenComponent: permanent monster regeneration (troll, troll_ancient).
        // Suppressed while AcidEffect OR BurningEffect is active on the entity.
        // PoC: regen suppressed when damage_type in ['acid', 'fire'] (fighter.py lines 553–570).
        if (entity.Get<InnateRegenComponent>() is { } innateRegen && fighter != null)
        {
            if (entity.Has<AcidEffect>() || entity.Has<BurningEffect>())
            {
                // Regen suppressed by acid or fire — emit event for presentation layer feedback.
                events.Add(new RegenSuppressedEvent { ActorId = entity.Id });
            }
            else
            {
                int actualHeal = fighter.Heal(innateRegen.HealPerTurn);
                if (actualHeal > 0)
                {
                    events.Add(new HotHealEvent
                    {
                        ActorId = entity.Id,
                        EntityId = entity.Id,
                        EffectName = "innate_regen",
                        Amount = actualHeal,
                    });
                }
            }
        }

        // ── EngulfedEffect adjacency refresh ──────────────────────────────
        // While engulfed, if any alive monster with EngulfsOnHitTag is adjacent, refresh
        // duration to 3. If no such monster is adjacent, duration ticks down normally.
        // Only meaningful when state is provided (not in unit tests with no monster list).
        //
        // IMPORTANT: Use state.Monsters directly (not state.AliveMonsters) to avoid
        // poisoning AliveMonsters cache. If state.AliveMonsters is called here (in the player's
        // ProcessTurnStart), the cache is built with all monsters alive. If a monster then dies
        // during the player's action and its Fighter component is removed by TransformToCorpse,
        // the stale cache in ResolveMonsterTurns would include the Fighter-less entity and crash.
        // Directly filtering state.Monsters with Get<Fighter>()?.IsAlive avoids caching.
        if (entity.Has<EngulfedEffect>() && state != null)
        {
            bool adjacentSlimeFound = state.Monsters.Any(m =>
                m.Get<Fighter>()?.IsAlive == true &&
                m.Has<EngulfsOnHitTag>() &&
                entity.ChebyshevDistanceTo(m.X, m.Y) <= 1);

            if (adjacentSlimeFound)
            {
                // Refresh — no event emitted (duration didn't expire; no presentation hook needed)
                entity.Require<EngulfedEffect>().RemainingTurns = 3;
            }
        }

        // ── Skip-turn checks ───────────────────────────────────────────────

        // FreeActionTag: entity is immune to slow and paralysis.
        // Check once here; the individual effect checks below are still run to decrement
        // their durations in ProcessTurnEnd — we just don't skip the turn.
        bool hasFreeAction = entity.Has<FreeActionTag>();

        // ImmobilizedEffect: always skip (unless FreeAction)
        if (entity.Has<ImmobilizedEffect>() && !hasFreeAction)
        {
            events.Add(new SkipTurnEvent
            {
                ActorId = entity.Id,
                EntityId = entity.Id,
                EffectName = "immobilized",
            });
            skipTurn = true;
        }

        // SleepEffect: always skip (woken by attack damage via OnDamageTaken)
        // SleepEffect is NOT blocked by FreeAction — free action only covers slow/paralysis.
        if (entity.Has<SleepEffect>())
        {
            events.Add(new SkipTurnEvent
            {
                ActorId = entity.Id,
                EntityId = entity.Id,
                EffectName = "sleep",
            });
            skipTurn = true;
        }

        // Unified alternating-skip gate (R2): SlowedEffect, EngulfedEffect, and
        // DissonantChantEffect all share one skip slot. If any of them is active AND
        // the turn is odd (turnCount % 2 == 1), exactly one skip fires. Stacking these
        // effects never causes multiple skips — the gate checks all three at once.
        // FreeAction still overrides all of them (covers slow/paralysis category only;
        // DissonantChant and Engulf are separate mechanics but use the same slot).
        //
        // Priority: first matching effect name is used in SkipTurnEvent (for presentation).
        if (!hasFreeAction && turnCount % 2 == 1 && !skipTurn)
        {
            string? alternatingSkipEffect = null;
            if (entity.Has<SlowedEffect>()) alternatingSkipEffect = "slowed";
            else if (entity.Has<EngulfedEffect>()) alternatingSkipEffect = "engulfed";
            else if (entity.Has<DissonantChantEffect>()) alternatingSkipEffect = "dissonant_chant";

            if (alternatingSkipEffect != null)
            {
                events.Add(new SkipTurnEvent
                {
                    ActorId = entity.Id,
                    EntityId = entity.Id,
                    EffectName = alternatingSkipEffect,
                });
                skipTurn = true;
            }
        }

        return skipTurn;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Turn End (duration decrement + expiry)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Decrement RemainingTurns on all non-permanent effects.
    /// Effects that reach 0 are removed and emit StatusExpiredEvent.
    ///
    /// Uses GetAllComponents().OfType&lt;IStatusEffect&gt;() to enumerate all effects without
    /// a hardcoded list — new effects are automatically handled when they implement IStatusEffect.
    /// Snapshot the list first because Remove() mutates the component dictionary.
    /// </summary>
    public static void ProcessTurnEnd(Entity entity, List<TurnEvent> events)
    {
        // Snapshot: toList prevents mutation-during-iteration if Remove changes the dictionary.
        var effects = entity.GetAllComponents().OfType<IStatusEffect>().ToList();

        foreach (var effect in effects)
        {
            if (effect.IsPermanent) continue;

            effect.RemainingTurns--;

            if (effect.RemainingTurns <= 0)
            {
                // Remove using the concrete runtime type. GetAllComponents returns the concrete
                // instance so GetType() gives us the exact key used by the entity's dictionary.
                RemoveEffect(entity, effect);
                events.Add(new StatusExpiredEvent
                {
                    ActorId = entity.Id,
                    EntityId = entity.Id,
                    EffectName = effect.EffectName,
                    Reason = "duration",
                });
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Wake on Damage
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when an entity takes ATTACK damage (not DOT damage).
    /// Wakes SleepEffect: removes it and emits StatusExpiredEvent with reason "woke_on_damage".
    ///
    /// DOT damage does NOT call this — poisoned sleeping entities stay asleep.
    /// This is PoC-verified behavior.
    /// </summary>
    public static void OnDamageTaken(Entity entity, List<TurnEvent> events)
    {
        if (entity.Has<SleepEffect>())
        {
            entity.Remove<SleepEffect>();
            events.Add(new StatusExpiredEvent
            {
                ActorId = entity.Id,
                EntityId = entity.Id,
                EffectName = "sleep",
                Reason = "woke_on_damage",
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Floor Transition
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Remove all status effects from an entity. Called when the player descends to a new floor.
    /// Policy: all effects clear on floor transition — no carry-over (simplest policy, avoids
    /// confusing persistent debuffs across dungeon floors).
    /// </summary>
    public static void ClearAllEffects(Entity entity)
    {
        var effects = entity.GetAllComponents().OfType<IStatusEffect>().ToList();
        foreach (var effect in effects)
            RemoveEffect(entity, effect);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Remove a specific status effect type from an entity.
    /// Used by OrcShamanAI to explicitly end DissonantChantEffect on the player when
    /// the channel ends naturally, is interrupted, or the shaman dies.
    /// </summary>
    public static void RemoveEffect<T>(Entity entity) where T : class, IStatusEffect
    {
        entity.Remove<T>();
    }

    /// <summary>
    /// Remove a status effect from an entity using its concrete runtime type.
    /// Entity.RemoveByType() accepts a Type key directly, so no per-type switch is needed.
    /// New effect types are handled automatically as long as they implement IStatusEffect —
    /// no registration required here.
    /// </summary>
    private static void RemoveEffect(Entity entity, IStatusEffect effect)
    {
        entity.RemoveByType(effect.GetType());
    }

    /// <summary>
    /// Apply bleed attraction: alert nearby undead monsters without a current target toward
    /// the bleeding entity. Cap: BleedAttractionCapPerTick per tick to prevent surging.
    ///
    /// Undead tag check: monster has tag "undead" in MonsterDefinition.Tags.
    /// Target filter: monsters without AlertedState (passive) and alive.
    ///
    /// Called from ProcessTurnStart when BleedEffect is active and state is provided.
    /// Sorted by distance (row-major as tiebreaker) for determinism.
    /// </summary>
    private static void ApplyBleedAttraction(Entity bleeder, GameState state, List<TurnEvent> events)
    {
        int cx = bleeder.X, cy = bleeder.Y;
        int radius = BleedEffect.BleedAttractionRadius;
        int cap = BleedEffect.BleedAttractionCapPerTick;

        // Find undead monsters within radius, without AlertedState, alive — sorted for determinism.
        var candidates = state.AliveMonsters
            .Where(m => m.Id != bleeder.Id
                && m.Get<AiComponent>()?.Tags?.Contains("undead") == true
                && !m.Has<AlertedState>()
                && Math.Abs(m.X - cx) <= radius
                && Math.Abs(m.Y - cy) <= radius)
            .OrderBy(m => Math.Abs(m.X - cx) + Math.Abs(m.Y - cy)) // nearest first
            .ThenBy(m => m.Y).ThenBy(m => m.X)                      // row-major tiebreaker
            .ToList();

        int alerted = 0;
        foreach (var candidate in candidates)
        {
            if (alerted >= cap) break;

            // Alert toward the bleeding entity.
            candidate.Add(new AlertedState
            {
                LastKnownPlayerX = cx,
                LastKnownPlayerY = cy,
                TurnsUntilDeaggro = 5,
            });
            alerted++;
        }
    }
}
