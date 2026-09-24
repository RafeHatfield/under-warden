using UnderWarden.Logic.AI;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

/// <summary>
/// Executes spell effects. Single source of truth for spell logic.
///
/// Entry point: Resolve() dispatches on SpellId to a private handler.
/// No virtual dispatch, no inheritance — one switch, data-driven.
/// All numeric parameters (damage, radius, range) come from SpellEffect,
/// not hardcoded in the resolver, so YAML can tune them without C# changes.
///
/// PoC reference: ~/development/rlike/spells/spell_executor.py
/// </summary>
public static class SpellResolver
{
    /// <summary>
    /// Resolve a spell. Called by TurnController when a scroll/wand is used.
    /// Returns all TurnEvents produced (SpellEvent, DamageEvents, MapRevealEvent, etc.).
    ///
    /// targetEntityId: for AutoClosest, the caller pre-resolves the closest monster and passes it.
    ///                 For Self/AoeSelf, null — the resolver uses caster position.
    /// </summary>
    public static List<TurnEvent> Resolve(
        Entity caster,
        SpellEffect spell,
        GameState state,
        int? targetEntityId = null,
        int? targetX = null,
        int? targetY = null,
        string? overrideSpellId = null)
    {
        // overrideSpellId is used when throwing a dual-mode potion: the item carries the drink
        // spell as its primary SpellId, but the caller (TurnController) passes the throw spell ID
        // here so we dispatch to the correct handler without mutating the SpellEffect component.
        string spellId = overrideSpellId ?? spell.SpellId;
        return spellId switch
        {
            "lightning"       => ResolveLightning(caster, spell, state, targetEntityId),
            "earthquake"      => ResolveEarthquake(caster, spell, state),
            "light"           => ResolveLight(caster, spell, state),
            "magic_mapping"   => ResolveMagicMapping(caster, spell, state),
            "detect_monsters" => ResolveDetectMonsters(caster, spell, state),
            "enhance_weapon"  => ResolveEnhanceWeapon(caster, spell, state),
            "enhance_armor"   => ResolveEnhanceArmor(caster, spell, state),
            // ── Phase 3: Single-target status effect spells ──────────────────
            // ConfusedEffect removed — DisorientationEffect is canonical for confused movement.
            "confusion"   => ResolveStatusEffect<DisorientationEffect>(caster, spell, state, targetEntityId,
                                statusName: "disoriented", duration: spell.Duration > 0 ? spell.Duration : 10,
                                applyEffect: (e, d) => e.GetOrAdd<DisorientationEffect>().RemainingTurns = d),
            "slow"        => ResolveStatusEffect<SlowedEffect>(caster, spell, state, targetEntityId,
                                statusName: "slowed", duration: spell.Duration > 0 ? spell.Duration : 10,
                                applyEffect: (e, d) => e.GetOrAdd<SlowedEffect>().RemainingTurns = d),
            "glue"        => ResolveStatusEffect<ImmobilizedEffect>(caster, spell, state, targetEntityId,
                                statusName: "immobilized", duration: spell.Duration > 0 ? spell.Duration : 5,
                                applyEffect: (e, d) => e.GetOrAdd<ImmobilizedEffect>().RemainingTurns = d),
            "rage"        => ResolveStatusEffect<EnragedEffect>(caster, spell, state, targetEntityId,
                                statusName: "enraged", duration: spell.Duration > 0 ? spell.Duration : 8,
                                applyEffect: (e, d) => e.GetOrAdd<EnragedEffect>().RemainingTurns = d),
            "yo_mama"     => ResolveYoMama(caster, spell, state, targetEntityId),
            "disarm"      => ResolveStatusEffect<DisarmedEffect>(caster, spell, state, targetEntityId,
                                statusName: "disarmed", duration: spell.Duration > 0 ? spell.Duration : 3,
                                applyEffect: (e, d) => e.GetOrAdd<DisarmedEffect>().RemainingTurns = d),
            "plague"      => ResolvePlague(caster, spell, state, targetEntityId),
            "aggravation" => ResolveAggravation(caster, spell, state, targetEntityId),
            // ── Phase 3: AoE / Self / Location spells ───────────────────────────────
            "fear"        => ResolveFear(caster, spell, state),
            "invisibility"=> ResolveSelfStatusEffect<InvisibilityEffect>(caster, spell, state,
                                statusName: "invisibility", duration: spell.Duration > 0 ? spell.Duration : 30,
                                applyEffect: (e, d) => e.GetOrAdd<InvisibilityEffect>().RemainingTurns = d),
            "shield"      => ResolveSelfStatusEffect<ShieldEffect>(caster, spell, state,
                                statusName: "shield", duration: spell.Duration > 0 ? spell.Duration : 10,
                                applyEffect: (e, d) => e.GetOrAdd<ShieldEffect>().RemainingTurns = d),
            // HasteEffect removed (not in PoC) — map "haste" spell to SpeedEffect for now.
            // SpeedEffect is inert until the bonus attack system lands (see SpeedEffect.cs TODO).
            "haste"       => ResolveSelfStatusEffect<SpeedEffect>(caster, spell, state,
                                statusName: "speed", duration: spell.Duration > 0 ? spell.Duration : 20,
                                applyEffect: (e, d) => e.GetOrAdd<SpeedEffect>().RemainingTurns = d),
            // ── Potion buff spells ───────────────────────────────────────────────────
            // "haste" and "invisibility" already handle speed/invisibility potions above.
            // drink_protection: ProtectionEffect with AcBonus=4, 50 turns (PoC: +4 AC / 50t).
            "drink_protection" => ResolveSelfStatusEffect<ProtectionEffect>(caster, spell, state,
                                statusName: "protection", duration: spell.Duration > 0 ? spell.Duration : 50,
                                applyEffect: (e, d) => { var fx = e.GetOrAdd<ProtectionEffect>(); fx.RemainingTurns = d; fx.AcBonus = 4; }),
            // drink_regeneration: RegenerationEffect with HealPerTurn=1, 50 turns (PoC: 1 HP/t / 50t).
            "drink_regeneration" => ResolveSelfStatusEffect<RegenerationEffect>(caster, spell, state,
                                statusName: "regeneration", duration: spell.Duration > 0 ? spell.Duration : 50,
                                applyEffect: (e, d) => { var fx = e.GetOrAdd<RegenerationEffect>(); fx.RemainingTurns = d; fx.HealPerTurn = 1; }),
            // drink_heroism: HeroismEffect with AttackBonus=3, DamageBonus=3, 30 turns (PoC: +3/+3/30t).
            "drink_heroism" => ResolveSelfStatusEffect<HeroismEffect>(caster, spell, state,
                                statusName: "heroism", duration: spell.Duration > 0 ? spell.Duration : 30,
                                applyEffect: (e, d) => { var fx = e.GetOrAdd<HeroismEffect>(); fx.RemainingTurns = d; fx.AttackBonus = 3; fx.DamageBonus = 3; }),
            "silence"     => ResolveStatusEffect<SilencedEffect>(caster, spell, state, targetEntityId,
                                statusName: "silenced", duration: spell.Duration > 0 ? spell.Duration : 3,
                                applyEffect: (e, d) => e.GetOrAdd<SilencedEffect>().RemainingTurns = d),
            "teleport"    => ResolveTeleport(caster, spell, state, targetX, targetY),
            "blink"       => ResolveBlink(caster, spell, state, targetX, targetY),
            "fireball"    => ResolveFireball(caster, spell, state, targetX, targetY),
            // raise_dead: location-targeted; raises a corpse on the map as an ally. Corpse
            // lifecycle is live (CorpseComponent + RaiseDeadResolver); returns "no corpse found"
            // only when no corpse is in range.
            "raise_dead"  => ResolveRaiseDead(caster, spell, state, targetX, targetY),
            // dragon_fart: cone of noxious gas — applies SleepEffect to all alive monsters in cone.
            // PoC: get_cone_tiles, 45° wide, range 8, caster-to-target direction.
            // Requires targetX/targetY (location targeting) to define cone direction.
            "dragon_fart" => ResolveDragonFart(caster, spell, state, targetX, targetY),
            // Identification scroll: identifies 1-3 random unidentified item types from inventory.
            // The scroll itself is identified by the TryIdentifyOnUse call in TurnController
            // (which fires after ResolveSpellAction returns). The handler here only handles
            // the secondary effect: picking additional unidentified types from inventory.
            "identify"    => ResolveIdentify(caster, spell, state),
            // ── Debuff potion spell IDs (drink = self, throw = target entity) ─────
            // PoC values verified: each pair uses the same effect/duration, different target.
            // drink_* applies the effect to the caster. throw_* applies to the target entity.
            "drink_weakness"  => ResolveSelfStatusEffect<WeaknessEffect>(caster, spell, state,
                                    statusName: "weakness", duration: spell.Duration > 0 ? spell.Duration : 30,
                                    applyEffect: (e, d) => e.GetOrAdd<WeaknessEffect>().RemainingTurns = d),
            "throw_weakness"  => ResolveStatusEffect<WeaknessEffect>(caster, spell, state, targetEntityId,
                                    statusName: "weakness", duration: spell.Duration > 0 ? spell.Duration : 30,
                                    applyEffect: (e, d) => e.GetOrAdd<WeaknessEffect>().RemainingTurns = d),
            // drink_slowness / throw_slowness: FreeActionTag blocks via StatusEffectProcessor.ApplyEffect.
            "drink_slowness"  => ResolveSelfStatusEffect<SlowedEffect>(caster, spell, state,
                                    statusName: "slowed", duration: spell.Duration > 0 ? spell.Duration : 20,
                                    applyEffect: (e, d) => StatusEffectProcessor.ApplyEffect<SlowedEffect>(e, d)),
            "throw_slowness"  => ResolveStatusEffect<SlowedEffect>(caster, spell, state, targetEntityId,
                                    statusName: "slowed", duration: spell.Duration > 0 ? spell.Duration : 20,
                                    applyEffect: (e, d) => StatusEffectProcessor.ApplyEffect<SlowedEffect>(e, d)),
            "drink_blindness" => ResolveSelfStatusEffect<BlindedEffect>(caster, spell, state,
                                    statusName: "blinded", duration: spell.Duration > 0 ? spell.Duration : 15,
                                    applyEffect: (e, d) => e.GetOrAdd<BlindedEffect>().RemainingTurns = d),
            "throw_blindness" => ResolveStatusEffect<BlindedEffect>(caster, spell, state, targetEntityId,
                                    statusName: "blinded", duration: spell.Duration > 0 ? spell.Duration : 15,
                                    applyEffect: (e, d) => e.GetOrAdd<BlindedEffect>().RemainingTurns = d),
            // drink_paralysis / throw_paralysis: random 3-5 turn duration (PoC value).
            "drink_paralysis" => ResolveParalysisPotion(caster, spell, state, targetEntityId: null),
            "throw_paralysis" => ResolveParalysisPotion(caster, spell, state, targetEntityId),
            "drink_tar"       => ResolveSelfStatusEffect<SluggishEffect>(caster, spell, state,
                                    statusName: "sluggish", duration: spell.Duration > 0 ? spell.Duration : 10,
                                    applyEffect: (e, d) => e.GetOrAdd<SluggishEffect>().RemainingTurns = d),
            "throw_tar"       => ResolveStatusEffect<SluggishEffect>(caster, spell, state, targetEntityId,
                                    statusName: "sluggish", duration: spell.Duration > 0 ? spell.Duration : 10,
                                    applyEffect: (e, d) => e.GetOrAdd<SluggishEffect>().RemainingTurns = d),
            // ── Dual-mode and special potion spell IDs ────────────────────────────
            // Root: drink = BarkskinEffect (+3 AC / 10t), throw = EntangledEffect (3t).
            "drink_root"      => ResolveSelfStatusEffect<BarkskinEffect>(caster, spell, state,
                                    statusName: "barkskin", duration: spell.Duration > 0 ? spell.Duration : 10,
                                    applyEffect: (e, d) => { var fx = e.GetOrAdd<BarkskinEffect>(); fx.RemainingTurns = d; fx.AcBonus = 3; }),
            "throw_root"      => ResolveStatusEffect<EntangledEffect>(caster, spell, state, targetEntityId,
                                    statusName: "entangled", duration: spell.Duration > 0 ? spell.Duration : 3,
                                    applyEffect: (e, d) => e.GetOrAdd<EntangledEffect>().RemainingTurns = d),
            // Sunburst: drink = FocusedEffect (+2 acc / 8t), throw = BlindedEffect (3t).
            "drink_sunburst"  => ResolveSelfStatusEffect<FocusedEffect>(caster, spell, state,
                                    statusName: "focused", duration: spell.Duration > 0 ? spell.Duration : 8,
                                    applyEffect: (e, d) => { var fx = e.GetOrAdd<FocusedEffect>(); fx.RemainingTurns = d; fx.AccuracyBonus = 2; }),
            "throw_sunburst"  => ResolveStatusEffect<BlindedEffect>(caster, spell, state, targetEntityId,
                                    statusName: "blinded", duration: spell.Duration > 0 ? spell.Duration : 3,
                                    applyEffect: (e, d) => e.GetOrAdd<BlindedEffect>().RemainingTurns = d),
            // Fire potion: throw-only — applies BurningEffect (1 dmg/turn / 4 turns). No drink spell.
            "throw_fire"      => ResolveStatusEffect<BurningEffect>(caster, spell, state, targetEntityId,
                                    statusName: "burning", duration: spell.Duration > 0 ? spell.Duration : 4,
                                    applyEffect: (e, d) => { var fx = e.GetOrAdd<BurningEffect>(); fx.RemainingTurns = d; fx.DamagePerTurn = 1; }),
            // Antidote: removes PlagueEffect immediately. No ongoing duration.
            "drink_antidote"  => ResolveAntidote(caster, spell, state),
            // Dispel (Hollowmark's Spell-Break): removes one IStatusEffect from target within range.
            // Priority: PossessionEffect (routes to PossessionSystem.OnPossessionDispelled) first,
            // then any other effect. Used by Sasha to free past-Sasha corpses (Variant 3).
            "dispel"          => ResolveDispel(caster, spell, state, targetEntityId),
            _ => [new SpellEvent
            {
                ActorId = caster.Id,
                SpellId = spell.SpellId,
                SpellName = spell.SpellId,
                Success = false,
            }],
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Lightning — AutoClosest: 40 dmg to the nearest visible enemy, range 5
    // PoC: TargetingType.SINGLE_ENEMY + _cast_auto_target_spell
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveLightning(
        Entity caster, SpellEffect spell, GameState state, int? targetEntityId)
    {
        var events = new List<TurnEvent>();

        // If the caller already resolved the target, use it; otherwise auto-find.
        Entity? target = null;
        if (targetEntityId.HasValue)
        {
            target = state.Monsters.FirstOrDefault(m => m.Id == targetEntityId.Value && m.Require<Fighter>().IsAlive);
        }
        else
        {
            target = FindClosestVisibleEnemy(caster, state, spell.Range > 0 ? spell.Range : 5);
        }

        if (target == null)
        {
            events.Add(new SpellEvent
            {
                ActorId = caster.Id,
                SpellId = spell.SpellId,
                SpellName = "Lightning",
                Success = false,
            });
            return events;
        }

        int damage = spell.Damage > 0 ? state.Rng.Next(spell.Damage / 2, spell.Damage + 1) : state.Rng.Next(10, 21);
        var targetFighter = target.Require<Fighter>();
        targetFighter.TakeDamage(damage);
        bool killed = !targetFighter.IsAlive;

        // Emit SpellEvent BEFORE DeathEvent so the lightning VFX plays before the target fades.
        events.Add(new SpellEvent
        {
            ActorId = caster.Id,
            SpellId = spell.SpellId,
            SpellName = "Lightning",
            TargetId = target.Id,
            Damage = damage,
            AffectedIds = [target.Id],
            Success = true,
            CasterPos = (caster.X, caster.Y),
            TargetPos = (target.X, target.Y),
            AffectedTiles = BresenhamLine(caster.X, caster.Y, target.X, target.Y),
        });

        if (killed)
        {
            events.Add(new DeathEvent { ActorId = target.Id, KillerId = caster.Id });
        }

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Earthquake — AoeSelf: 20 dmg to ALL visible enemies within radius 3
    // PoC: AoE centered on caster, no self-damage
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveEarthquake(Entity caster, SpellEffect spell, GameState state)
    {
        var events = new List<TurnEvent>();
        int radius = spell.Radius > 0 ? spell.Radius : 3;
        int baseDamage = spell.Damage > 0 ? spell.Damage : 20;

        var affected = new List<Entity>();
        var affectedIds = new List<int>();

        // Friendly-fire guard (TASK-006): a player-side caster's AoE never hits Sasha's side
        // (player-allies). Monster-cast AoE is unaffected — preserves existing behavior.
        string casterFaction = caster.Get<AiComponent>()?.Faction ?? FactionRegistry.PlayerFaction;
        bool casterIsPlayerSide = FactionRegistry.IsPlayerSide(casterFaction);

        foreach (var monster in state.AliveMonsters)
        {
            // Skip Sasha's side — unless a (Warden-)enraged ally, which is hostile to all and
            // is therefore a legitimate target the player can fight back against (TASK-006/007).
            if (casterIsPlayerSide
                && FactionRegistry.IsPlayerSide(monster.Get<AiComponent>()?.Faction ?? "neutral")
                && !monster.Has<EnragedEffect>())
                continue;

            // Check within radius (Chebyshev distance for consistent roguelike feel)
            int dist = caster.ChebyshevDistanceTo(monster.X, monster.Y);
            if (dist > radius) continue;

            // Earthquake affects visible enemies only (player can't hit what they can't see).
            // In scenario mode (IsDungeonMode=false) all tiles are pre-revealed, so skip
            // the visibility check — it always returns false for unlit scenario maps.
            if (state.IsDungeonMode && !state.Map.IsVisible(monster.X, monster.Y)) continue;

            affected.Add(monster);
        }

        // Roll damage once per monster (variable, not the same roll for all)
        foreach (var target in affected)
        {
            int damage = state.Rng.Next(baseDamage / 2, baseDamage + 1);
            var targetFighter = target.Require<Fighter>();
            targetFighter.TakeDamage(damage);
            bool killed = !targetFighter.IsAlive;

            affectedIds.Add(target.Id);

            if (killed)
            {
                events.Add(new DeathEvent { ActorId = target.Id, KillerId = caster.Id });
            }
        }

        events.Add(new SpellEvent
        {
            ActorId = caster.Id,
            SpellId = spell.SpellId,
            SpellName = "Earthquake",
            Damage = baseDamage,
            AffectedIds = affectedIds,
            Success = affectedIds.Count > 0,
        });

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Light — Self: marks all currently-visible tiles as explored (permanent)
    // PoC: reveals current room's tiles in FOV
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveLight(Entity caster, SpellEffect spell, GameState state)
    {
        var events = new List<TurnEvent>();

        // In dungeon mode, all currently visible tiles are marked explored.
        // In scenario mode (no FOV), RevealAll is already called — this is a no-op.
        // The effect is "you permanently know every tile you can see right now,"
        // which is meaningful in dark areas that would otherwise un-fog.
        if (state.IsDungeonMode)
        {
            // GameMap.SetVisible already marks tiles as explored. The Light scroll's
            // effect is to mark visible tiles explored even if FOV expires this turn.
            // Since SetVisible already does this, the map state is already correct —
            // the event is what signals the UI to show the "light scroll" flash.
        }

        events.Add(new MapRevealEvent
        {
            ActorId = caster.Id,
            RevealType = "fov",
        });

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Magic Mapping — Self: marks ALL tiles on the floor as explored
    // PoC: reveals entire floor immediately
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveMagicMapping(Entity caster, SpellEffect spell, GameState state)
    {
        var events = new List<TurnEvent>();

        // Mark every tile on the current floor as explored (but NOT visible this turn).
        // Explored = shown on minimap / remembered. Visible = currently in FOV.
        // RevealAll sets both, but magic mapping only sets explored, not visible.
        // We need a targeted "set explored" loop — GameMap exposes SetExplored per-tile.
        // Use RevealAll as it's the available API — it sets both explored and visible,
        // which is consistent with PoC behavior (magic map shows the whole floor).
        state.Map.RevealAll();

        events.Add(new MapRevealEvent
        {
            ActorId = caster.Id,
            RevealType = "full",
        });

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Detect Monsters — Self: broadcasts all monster positions
    // PoC: all monsters briefly visible through walls for 20 turns
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveDetectMonsters(Entity caster, SpellEffect spell, GameState state)
    {
        var events = new List<TurnEvent>();
        int duration = spell.Duration > 0 ? spell.Duration : 20;

        var positions = state.AliveMonsters
            .Select(m => (m.X, m.Y, m.Id))
            .ToList();

        events.Add(new DetectMonstersEvent
        {
            ActorId = caster.Id,
            MonsterPositions = positions,
            Duration = duration,
        });

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Enhance Weapon — Self: +1 min / +2 max dmg to equipped main-hand weapon
    // PoC: enhance_weapon scroll adds +1 to weapon damage range
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveEnhanceWeapon(Entity caster, SpellEffect spell, GameState state)
    {
        var events = new List<TurnEvent>();
        var equip = caster.Get<Equipment>();
        var weapon = equip?.MainHand?.Get<Equippable>();

        if (weapon == null || !weapon.IsWeapon)
        {
            // Scroll is wasted — no weapon equipped. Still consumed.
            events.Add(new SpellEvent
            {
                ActorId = caster.Id,
                SpellId = spell.SpellId,
                SpellName = "Enhance Weapon",
                Success = false,
            });
            return events;
        }

        // +1 DamageMin, +2 DamageMax (PoC: "enhance" means a meaningful bump, not just +1)
        weapon.DamageMin += 1;
        weapon.DamageMax += 2;

        events.Add(new SpellEvent
        {
            ActorId = caster.Id,
            SpellId = spell.SpellId,
            SpellName = "Enhance Weapon",
            Success = true,
        });

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Enhance Armor — Self: +1 AC to a random equipped armor piece
    // PoC: picks a random equipped armor slot and bumps its ArmorClassBonus
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveEnhanceArmor(Entity caster, SpellEffect spell, GameState state)
    {
        var events = new List<TurnEvent>();
        var equip = caster.Get<Equipment>();

        // Collect all equipped armor pieces (slots other than MainHand weapon)
        var armorPieces = new List<Equippable>();
        if (equip != null)
        {
            var candidates = new[] { equip.OffHand, equip.Head, equip.Chest, equip.Feet };
            foreach (var slot in candidates)
            {
                var eq = slot?.Get<Equippable>();
                if (eq != null && !eq.IsWeapon)
                    armorPieces.Add(eq);
            }
        }

        if (armorPieces.Count == 0)
        {
            // No armor equipped — scroll wasted
            events.Add(new SpellEvent
            {
                ActorId = caster.Id,
                SpellId = spell.SpellId,
                SpellName = "Enhance Armor",
                Success = false,
            });
            return events;
        }

        // Pick a random armor piece to enhance
        var chosen = armorPieces[state.Rng.Next(armorPieces.Count)];
        chosen.ArmorClassBonus += 1;

        events.Add(new SpellEvent
        {
            ActorId = caster.Id,
            SpellId = spell.SpellId,
            SpellName = "Enhance Armor",
            Success = true,
        });

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Find the closest alive monster visible to the caster within the given range.
    /// Uses Euclidean distance (matches PoC distance_to behavior).
    /// Returns null if no visible enemies are in range.
    /// </summary>
    private static Entity? FindClosestVisibleEnemy(Entity caster, GameState state, int maxRange)
    {
        Entity? closest = null;
        double closestDist = maxRange + 1.0; // exclusive upper bound

        // Friendly-fire guard (TASK-006): a player-side caster never auto-targets Sasha's side.
        string casterFaction = caster.Get<AiComponent>()?.Faction ?? FactionRegistry.PlayerFaction;
        bool casterIsPlayerSide = FactionRegistry.IsPlayerSide(casterFaction);

        foreach (var monster in state.AliveMonsters)
        {
            // Skip Sasha's side — unless a (Warden-)enraged ally that has turned hostile.
            if (casterIsPlayerSide
                && FactionRegistry.IsPlayerSide(monster.Get<AiComponent>()?.Faction ?? "neutral")
                && !monster.Has<EnragedEffect>())
                continue;

            // In dungeon mode, check FOV visibility. In scenario mode (no FOV), all tiles visible.
            if (state.IsDungeonMode && !state.Map.IsVisible(monster.X, monster.Y))
                continue;

            double dist = caster.DistanceTo(monster.X, monster.Y);
            if (dist < closestDist)
            {
                closest = monster;
                closestDist = dist;
            }
        }

        return closest;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Phase 3: Single-target status effect spells
    //
    // All follow the same pattern:
    //   1. Look up target by targetEntityId (provided by UI after player taps a monster).
    //   2. Validate it's alive and in range.
    //   3. Apply the status effect component.
    //   4. Emit SpellEvent with StatusApplied.
    //
    // The targeting UI (Phase 3 presentation work) validates range before creating the
    // CastSpell action, so range validation here is a defense-in-depth check.
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generic single-target status effect resolver.
    /// Shared by confusion, slow, glue, rage, disarm — all follow the same pattern.
    /// applyEffect receives (targetEntity, duration) to apply the status component.
    /// </summary>
    private static List<TurnEvent> ResolveStatusEffect<TEffect>(
        Entity caster, SpellEffect spell, GameState state,
        int? targetEntityId, string statusName, int duration,
        Action<Entity, int> applyEffect)
        where TEffect : class, UnderWarden.Logic.ECS.IComponent
    {
        var events = new List<TurnEvent>();
        var target = FindTargetById(state, targetEntityId, spell.Range > 0 ? spell.Range : 8, caster);

        if (target == null)
        {
            events.Add(new SpellEvent
            {
                ActorId = caster.Id, SpellId = spell.SpellId,
                SpellName = statusName, Success = false,
            });
            return events;
        }

        applyEffect(target, duration);

        events.Add(new SpellEvent
        {
            ActorId = caster.Id,
            SpellId = spell.SpellId,
            SpellName = statusName,
            TargetId = target.Id,
            AffectedIds = [target.Id],
            Success = true,
            StatusApplied = statusName,
            StatusDuration = duration,
        });

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Yo Mama — SingleTarget: apply permanent TauntedEffect targeting the CASTER
    // PoC: target yells insult, ALL hostiles attack them (yo_mama_scroll)
    // Effect: the targeted monster is taunted into attacking the caster
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveYoMama(
        Entity caster, SpellEffect spell, GameState state, int? targetEntityId)
    {
        var events = new List<TurnEvent>();
        var target = FindTargetById(state, targetEntityId, spell.Range > 0 ? spell.Range : 10, caster);

        if (target == null)
        {
            events.Add(new SpellEvent
            {
                ActorId = caster.Id, SpellId = spell.SpellId,
                SpellName = "Yo Mama", Success = false,
            });
            return events;
        }

        // Target becomes permanently fixated on the caster (e.g., the player taunted an orc)
        var effect = target.GetOrAdd<TauntedEffect>();
        effect.TauntTargetId = caster.Id;
        effect.RemainingTurns = -1; // permanent

        events.Add(new SpellEvent
        {
            ActorId = caster.Id,
            SpellId = spell.SpellId,
            SpellName = "Yo Mama",
            TargetId = target.Id,
            AffectedIds = [target.Id],
            Success = true,
            StatusApplied = "taunted",
            StatusDuration = -1, // permanent
        });

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Plague — SingleTarget: infect corporeal target with 1 dmg/turn for 20 turns
    // PoC: plague_scroll — only works on living/corporeal creatures
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolvePlague(
        Entity caster, SpellEffect spell, GameState state, int? targetEntityId)
    {
        var events = new List<TurnEvent>();
        var target = FindTargetById(state, targetEntityId, spell.Range > 0 ? spell.Range : 8, caster);

        if (target == null)
        {
            events.Add(new SpellEvent
            {
                ActorId = caster.Id, SpellId = spell.SpellId,
                SpellName = "Plague", Success = false,
            });
            return events;
        }

        // Plague only affects corporeal creatures — check tags.
        // In scenario mode (no MonsterDefinition lookup), we treat all monsters as corporeal
        // unless they have an "undead" tag that implies non-corporeal (skeletons, ghosts).
        // The tag check uses the PoC convention: "corporeal_flesh" tag = valid target.
        // Fallback: check AiComponent tags if present; otherwise assume corporeal.
        bool isCorporeal = IsCorporeal(target, state);
        if (!isCorporeal)
        {
            events.Add(new SpellEvent
            {
                ActorId = caster.Id,
                SpellId = spell.SpellId,
                SpellName = "Plague",
                TargetId = target.Id,
                Success = false,
                StatusApplied = "",
                StatusDuration = 0,
            });
            return events;
        }

        int duration = spell.Duration > 0 ? spell.Duration : 20;
        int dmgPerTurn = 1;

        var effect = target.GetOrAdd<PlagueEffect>();
        effect.RemainingTurns = duration;
        effect.DamagePerTurn = dmgPerTurn;

        events.Add(new SpellEvent
        {
            ActorId = caster.Id,
            SpellId = spell.SpellId,
            SpellName = "Plague",
            TargetId = target.Id,
            AffectedIds = [target.Id],
            Success = true,
            StatusApplied = "plague",
            StatusDuration = duration,
        });

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Aggravation — SingleTarget: target permanently aggros against their own faction
    // PoC: aggravation_scroll — incites monster to attack a faction (usually their own)
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveAggravation(
        Entity caster, SpellEffect spell, GameState state, int? targetEntityId)
    {
        var events = new List<TurnEvent>();
        var target = FindTargetById(state, targetEntityId, spell.Range > 0 ? spell.Range : 10, caster);

        if (target == null)
        {
            events.Add(new SpellEvent
            {
                ActorId = caster.Id, SpellId = spell.SpellId,
                SpellName = "Aggravation", Success = false,
            });
            return events;
        }

        // Determine the target's faction from AiComponent; default to "" if unknown.
        // The aggravated monster attacks all members of its own faction (e.g., an orc
        // aggravated against "orc" faction will attack other orcs).
        var ai = target.Get<UnderWarden.Logic.ECS.AiComponent>();
        string targetFaction = ai?.Faction ?? "";

        var effect = target.GetOrAdd<AggravatedEffect>();
        effect.TargetFaction = targetFaction;
        effect.RemainingTurns = -1; // permanent

        events.Add(new SpellEvent
        {
            ActorId = caster.Id,
            SpellId = spell.SpellId,
            SpellName = "Aggravation",
            TargetId = target.Id,
            AffectedIds = [target.Id],
            Success = true,
            StatusApplied = "aggravated",
            StatusDuration = -1, // permanent
        });

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Fear — AoeSelf: apply FearEffect to all visible monsters within radius
    // PoC: fear.targeting = AOE, radius = 10, duration = 15, no target required
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveFear(Entity caster, SpellEffect spell, GameState state)
    {
        var events = new List<TurnEvent>();
        int radius = spell.Radius > 0 ? spell.Radius : 10;
        int duration = spell.Duration > 0 ? spell.Duration : 15;
        var affectedIds = new List<int>();

        foreach (var monster in state.AliveMonsters)
        {
            double dist = caster.DistanceTo(monster.X, monster.Y);
            if (dist > radius) continue;

            // Scenario mode (no FOV): affect all in radius. Dungeon mode: visible only.
            if (state.IsDungeonMode && !state.Map.IsVisible(monster.X, monster.Y)) continue;

            monster.GetOrAdd<FearEffect>().RemainingTurns = duration;
            affectedIds.Add(monster.Id);

            events.Add(new StatusAppliedEvent
            {
                ActorId = caster.Id,
                TargetId = monster.Id,
                EffectName = "fear",
                Duration = duration,
            });
        }

        events.Add(new SpellEvent
        {
            ActorId = caster.Id,
            SpellId = spell.SpellId,
            SpellName = "Fear",
            AffectedIds = affectedIds,
            Success = affectedIds.Count > 0,
            StatusApplied = "fear",
            StatusDuration = duration,
        });

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Self status effect — shared by invisibility, shield, haste
    // Applies the effect to the CASTER, not a targeted enemy.
    // Identical to ResolveStatusEffect but operates on the caster instead of a target.
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveSelfStatusEffect<TEffect>(
        Entity caster, SpellEffect spell, GameState state,
        string statusName, int duration,
        Action<Entity, int> applyEffect)
        where TEffect : class, IComponent
    {
        applyEffect(caster, duration);

        return [new SpellEvent
        {
            ActorId = caster.Id,
            SpellId = spell.SpellId,
            SpellName = statusName,
            Success = true,
            StatusApplied = statusName,
            StatusDuration = duration,
        }];
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Teleport — Location: move caster to target tile; 10% misfire → random tile
    // PoC: teleport.targeting = LOCATION, max_range = 20, misfire_chance = 0.10
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveTeleport(
        Entity caster, SpellEffect spell, GameState state, int? targetX, int? targetY)
    {
        var events = new List<TurnEvent>();
        double misfireChance = spell.MisfireChance > 0 ? spell.MisfireChance : 0.10;
        int fromX = caster.X, fromY = caster.Y;

        bool misfire = state.Rng.NextDouble() < misfireChance;

        int destX, destY;
        if (misfire || !targetX.HasValue || !targetY.HasValue
            || !state.Map.IsWalkable(targetX.Value, targetY.Value))
        {
            // Misfire or no valid target: pick a random walkable tile
            var fallback = FindRandomWalkableTile(state, state.Rng);
            destX = fallback.X;
            destY = fallback.Y;
        }
        else
        {
            destX = targetX.Value;
            destY = targetY.Value;
        }

        caster.X = destX;
        caster.Y = destY;

        // On misfire: apply DisorientationEffect for 3 turns.
        // PoC: misfire = disorienting experience of landing somewhere unexpected.
        // Kept short (3 turns) so it's punishing but not crippling.
        if (misfire)
        {
            caster.GetOrAdd<DisorientationEffect>().RemainingTurns = 3;
        }

        events.Add(new TeleportEvent
        {
            ActorId = caster.Id,
            EntityId = caster.Id,
            FromX = fromX,
            FromY = fromY,
            ToX = destX,
            ToY = destY,
            Misfire = misfire,
        });

        events.Add(new SpellEvent
        {
            ActorId = caster.Id,
            SpellId = spell.SpellId,
            SpellName = "Teleport",
            Success = true,
        });

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Blink — Location: short-range teleport, no misfire chance
    // PoC: blink.targeting = LOCATION, max_range = 5
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveBlink(
        Entity caster, SpellEffect spell, GameState state, int? targetX, int? targetY)
    {
        var events = new List<TurnEvent>();
        int maxRange = spell.Range > 0 ? spell.Range : 5;
        int fromX = caster.X, fromY = caster.Y;

        if (!targetX.HasValue || !targetY.HasValue || !state.Map.IsWalkable(targetX.Value, targetY.Value))
        {
            events.Add(new SpellEvent
            {
                ActorId = caster.Id, SpellId = spell.SpellId, SpellName = "Blink", Success = false,
            });
            return events;
        }

        double dist = caster.DistanceTo(targetX.Value, targetY.Value);
        if (dist > maxRange)
        {
            events.Add(new SpellEvent
            {
                ActorId = caster.Id, SpellId = spell.SpellId, SpellName = "Blink", Success = false,
            });
            return events;
        }

        caster.X = targetX.Value;
        caster.Y = targetY.Value;

        events.Add(new TeleportEvent
        {
            ActorId = caster.Id,
            EntityId = caster.Id,
            FromX = fromX,
            FromY = fromY,
            ToX = targetX.Value,
            ToY = targetY.Value,
            Misfire = false,
        });

        events.Add(new SpellEvent
        {
            ActorId = caster.Id, SpellId = spell.SpellId, SpellName = "Blink", Success = true,
        });

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Fireball — Location AoE: 25 damage, radius 3, at target location
    // PoC: fireball.damage = "3d6" (~10.5 avg), radius = 3, max_range = 10
    // Plan spec uses flat 25 damage — YAML can tune via damage field.
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveFireball(
        Entity caster, SpellEffect spell, GameState state, int? targetX, int? targetY)
    {
        var events = new List<TurnEvent>();
        int radius = spell.Radius > 0 ? spell.Radius : 3;
        int baseDamage = spell.Damage > 0 ? spell.Damage : 25;

        if (!targetX.HasValue || !targetY.HasValue)
        {
            events.Add(new SpellEvent
            {
                ActorId = caster.Id, SpellId = spell.SpellId, SpellName = "Fireball", Success = false,
            });
            return events;
        }

        int cx = targetX.Value, cy = targetY.Value;
        var affectedIds = new List<int>();
        var deaths = new List<DeathEvent>();

        foreach (var monster in state.AliveMonsters)
        {
            // Chebyshev distance from explosion center (same as Earthquake for consistency)
            int dist = Math.Max(Math.Abs(monster.X - cx), Math.Abs(monster.Y - cy));
            if (dist > radius) continue;

            int damage = state.Rng.Next(baseDamage / 2, baseDamage + 1);
            var targetFighter = monster.Require<Fighter>();
            targetFighter.TakeDamage(damage);
            bool killed = !targetFighter.IsAlive;
            affectedIds.Add(monster.Id);

            if (killed)
                deaths.Add(new DeathEvent { ActorId = monster.Id, KillerId = caster.Id });
        }

        // Compute the full blast area for VFX (Chebyshev radius, clamped to map bounds).
        // SpellEvent comes BEFORE DeathEvents so the fireball VFX plays first.
        var blastTiles = ChebyshevArea(cx, cy, radius, state.Map.Width, state.Map.Height);

        events.Add(new SpellEvent
        {
            ActorId = caster.Id,
            SpellId = spell.SpellId,
            SpellName = "Fireball",
            Damage = baseDamage,
            AffectedIds = affectedIds,
            Success = true,
            CasterPos = (caster.X, caster.Y),
            TargetPos = (cx, cy),
            AffectedTiles = blastTiles,
        });

        events.AddRange(deaths);

        // Leave burning ground on all blast tiles: base 3 damage, lasts 3 turns (3→2→1).
        foreach (var (tx, ty) in blastTiles)
            state.GroundHazards.AddHazard(HazardType.Fire, tx, ty, baseDamage: 3, maxDuration: 3);

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Raise Dead — Location: stub. Resurrect a corpse as a mindless zombie.
    // PoC: raise_dead_scroll — requires corpse entities at target location.
    // Corpse lifecycle is part of plan_monster_specials. Until that system lands,
    // this always returns "no valid corpse found" (scroll is consumed, nothing happens).
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveRaiseDead(
        Entity caster, SpellEffect spell, GameState state, int? targetX, int? targetY)
    {
        if (targetX == null || targetY == null)
            return [new SpellEvent { ActorId = caster.Id, SpellId = spell.SpellId, SpellName = "Raise Dead", Success = false }];

        // Range check (Euclidean)
        double dist = RaiseDeadResolver.DistanceTo(caster, targetX.Value, targetY.Value);
        if (dist > spell.Range)
            return [new SpellEvent { ActorId = caster.Id, SpellId = spell.SpellId, SpellName = "Raise Dead", Success = false }];

        // Find raisable corpse at target tile
        var corpse = state.Corpses.FirstOrDefault(c =>
            c.X == targetX.Value && c.Y == targetY.Value &&
            c.Get<CorpseComponent>()?.CanBeRaised == true);

        if (corpse == null)
            return [new SpellEvent { ActorId = caster.Id, SpellId = spell.SpellId, SpellName = "Raise Dead", Success = false }];

        var corpseComp = corpse.Require<CorpseComponent>();
        string corpseId = corpseComp.CorpseId;

        RaiseDeadResolver.Raise(corpse, casterFaction: "player", state);

        return [new SpellEvent
        {
            ActorId = caster.Id,
            SpellId = spell.SpellId,
            SpellName = "Raise Dead",
            Success = true,
        }, new RaiseDeadEvent
        {
            ActorId = caster.Id,
            RaisedEntityId = corpse.Id,
            CorpseId = corpseId,
            AssignedFaction = "neutral",
        }];
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Dragon Fart — Location AoE: cone of noxious gas, applies SleepEffect
    // PoC: get_cone_tiles, 45° wide, range 8, caster-to-target direction.
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveDragonFart(
        Entity caster, SpellEffect spell, GameState state, int? targetX, int? targetY)
    {
        var events = new List<TurnEvent>();
        int duration = spell.Duration > 0 ? spell.Duration : 3;
        int range = spell.Range > 0 ? spell.Range : 8;

        if (!targetX.HasValue || !targetY.HasValue)
        {
            events.Add(new SpellEvent
            {
                ActorId = caster.Id, SpellId = spell.SpellId, SpellName = "Dragon Fart", Success = false,
            });
            return events;
        }

        var coneTiles = GetConeTiles(caster.X, caster.Y, targetX.Value, targetY.Value,
            maxRange: range, coneWidthDegrees: 60);
        // Always include the target tile itself — ensures VFX shows even at point-blank range.
        coneTiles.Add((targetX.Value, targetY.Value));

        var affectedIds = new List<int>();

        // Apply SleepEffect to all alive monsters within the cone.
        foreach (var monster in state.AliveMonsters)
        {
            if (!coneTiles.Contains((monster.X, monster.Y))) continue;
            monster.GetOrAdd<SleepEffect>().RemainingTurns = duration;
            affectedIds.Add(monster.Id);
            events.Add(new StatusAppliedEvent
            {
                ActorId = caster.Id,
                TargetId = monster.Id,
                EffectName = "sleep",
                Duration = duration,
            });
        }

        // SpellEvent first so VFX plays before targets update.
        // Insert SpellEvent at the front, before the StatusAppliedEvents.
        events.Insert(0, new SpellEvent
        {
            ActorId = caster.Id,
            SpellId = spell.SpellId,
            SpellName = "Dragon Fart",
            AffectedIds = affectedIds,
            Success = true,
            StatusApplied = "sleep",
            StatusDuration = duration,
            CasterPos = (caster.X, caster.Y),
            TargetPos = (targetX.Value, targetY.Value),
            AffectedTiles = coneTiles.ToList(),
        });

        // Leave poison gas on all cone tiles: base 6 damage, lasts 5 turns.
        foreach (var (tx, ty) in coneTiles)
            state.GroundHazards.AddHazard(HazardType.PoisonGas, tx, ty, baseDamage: 6, maxDuration: 5);

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Geometry helpers (pure math, no Godot)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute all tiles within Chebyshev distance 'radius' of center, clamped to map bounds.
    /// Used for fireball blast area VFX.
    /// </summary>
    private static IReadOnlyList<(int X, int Y)> ChebyshevArea(int cx, int cy, int radius, int mapWidth, int mapHeight)
    {
        var tiles = new List<(int, int)>();
        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
        {
            // Chebyshev distance = max(|dx|, |dy|)
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) > radius) continue;
            int tx = cx + dx, ty = cy + dy;
            if (tx >= 0 && tx < mapWidth && ty >= 0 && ty < mapHeight)
                tiles.Add((tx, ty));
        }
        return tiles;
    }

    /// <summary>
    /// Standard integer Bresenham line from (x0,y0) to (x1,y1), including both endpoints.
    /// Used for lightning bolt VFX path.
    /// </summary>
    private static IReadOnlyList<(int X, int Y)> BresenhamLine(int x0, int y0, int x1, int y1)
    {
        var tiles = new List<(int, int)>();

        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        int x = x0, y = y0;
        while (true)
        {
            tiles.Add((x, y));
            if (x == x1 && y == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }

        return tiles;
    }

    /// <summary>
    /// Compute tiles within a cone from (ox,oy) toward (tx,ty), with given width (degrees) and range.
    /// Ported from PoC get_cone_tiles (item_functions.py). Returns an empty HashSet if
    /// origin == target (no direction defined).
    /// </summary>
    private static HashSet<(int X, int Y)> GetConeTiles(
        int ox, int oy, int tx, int ty, int maxRange = 8, double coneWidthDegrees = 45.0)
    {
        var result = new HashSet<(int, int)>();

        int ddx = tx - ox, ddy = ty - oy;
        if (ddx == 0 && ddy == 0) return result; // no direction — empty cone

        double targetAngle = Math.Atan2(ddy, ddx);
        double halfWidth = Math.PI * (coneWidthDegrees / 2.0) / 180.0;

        for (int distance = 1; distance <= maxRange; distance++)
        {
            int widthAtDistance = (int)(distance * Math.Tan(halfWidth));

            for (int offset = -widthAtDistance; offset <= widthAtDistance; offset++)
            {
                double angle = targetAngle + Math.Atan2(offset, distance);
                int cx = ox + (int)(distance * Math.Cos(angle));
                int cy = oy + (int)(distance * Math.Sin(angle));

                // Verify the candidate tile actually falls within the cone angle
                int tileDx = cx - ox, tileDy = cy - oy;
                double tileAngle = Math.Atan2(tileDy, tileDx);
                double diff = tileAngle - targetAngle;

                // Normalize to [-π, π]
                while (diff > Math.PI)  diff -= 2 * Math.PI;
                while (diff < -Math.PI) diff += 2 * Math.PI;

                if (Math.Abs(diff) <= halfWidth)
                    result.Add((cx, cy));
            }
        }

        return result;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Phase 3 Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Find a target monster by entity ID, validating it's alive and within range.
    /// If targetEntityId is null, returns null (single-target spells require an explicit target).
    /// Range uses Euclidean distance (matches PoC distance_to).
    /// </summary>
    private static Entity? FindTargetById(GameState state, int? targetEntityId, double maxRange, Entity caster)
    {
        if (!targetEntityId.HasValue) return null;

        var target = state.Monsters.FirstOrDefault(m =>
            m.Id == targetEntityId.Value && m.Require<Fighter>().IsAlive);
        if (target == null) return null;

        // Validate range
        if (maxRange > 0 && caster.DistanceTo(target.X, target.Y) > maxRange)
            return null;

        return target;
    }

    /// <summary>
    /// Check whether a target entity is corporeal (flesh-and-blood, susceptible to plague).
    /// Non-corporeal: skeletons, ghosts, constructs.
    /// Corporeal: orcs, zombies, slimes, most living creatures.
    ///
    /// Checks AiComponent.Tags for "undead_bone" or "construct" which marks non-corporeal.
    /// Zombies and slimes have flesh so they ARE corporeal.
    /// In scenario/test mode (no tags on entity) — assume corporeal (safe default).
    /// </summary>
    private static bool IsCorporeal(Entity target, GameState state)
    {
        var ai = target.Get<UnderWarden.Logic.ECS.AiComponent>();
        if (ai?.Tags == null) return true; // no tags → assume corporeal

        // Non-corporeal creature types
        bool isNonCorporeal = ai.Tags.Contains("undead_bone")
            || ai.Tags.Contains("construct")
            || ai.Tags.Contains("ghost")
            || ai.Tags.Contains("ethereal");

        return !isNonCorporeal;
    }

    /// <summary>
    /// Find a random walkable tile on the map. Used by teleport misfire.
    /// Collects all walkable tiles and picks one at random.
    /// Falls back to caster position if no walkable tiles found (shouldn't happen).
    /// </summary>
    private static (int X, int Y) FindRandomWalkableTile(GameState state, SeededRandom rng)
    {
        var candidates = new List<(int X, int Y)>();
        for (int x = 0; x < state.Map.Width; x++)
            for (int y = 0; y < state.Map.Height; y++)
                if (state.Map.IsWalkable(x, y))
                    candidates.Add((x, y));

        if (candidates.Count == 0) return (state.Player.X, state.Player.Y);
        return candidates[rng.Next(candidates.Count)];
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Identify — Self: identifies 1-3 random unidentified item types from inventory.
    //
    // The scroll itself is identified by TurnController.TryIdentifyOnUse (which fires
    // after ResolveSpellAction returns). This handler only covers the secondary effect:
    // picking additional unidentified types from inventory.
    //
    // Flow:
    //   1. If no identification registry: return success (no-op, scenario mode)
    //   2. Gather all unidentified types present in player inventory (deduplicated by TypeId)
    //   3. If none: emit success SpellEvent with StatusApplied="none" (scroll consumed, nothing happens)
    //   4. Else: randomly select Min(count, Rng.Next(1,4)) types and identify them
    //   5. Emit IdentificationEvent for each + aggregate success SpellEvent
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveIdentify(Entity caster, SpellEffect spell, GameState state)
    {
        var events = new List<TurnEvent>();
        var registry = state.IdentificationRegistry;

        // Scenario mode: no registry — scroll fires as a no-op but is consumed and identified.
        if (registry == null)
        {
            events.Add(new Core.SpellEvent
            {
                ActorId   = caster.Id,
                SpellId   = spell.SpellId,
                SpellName = "Identify",
                Success   = true,
            });
            return events;
        }

        var inventory = state.Player.Get<ECS.Inventory>();
        if (inventory == null)
        {
            events.Add(new Core.SpellEvent
            {
                ActorId   = caster.Id,
                SpellId   = spell.SpellId,
                SpellName = "Identify",
                Success   = true,
                StatusApplied = "none",
            });
            return events;
        }

        // Gather all unidentified item types currently in inventory (distinct TypeIds).
        var unidentifiedTypes = inventory.Items
            .Select(i => (item: i, tag: i.Get<ECS.ItemTag>(), idComp: i.Get<ECS.IdentifiableItem>()))
            .Where(t => t.tag != null && t.idComp != null && !registry.IsIdentified(t.tag.TypeId))
            .GroupBy(t => t.tag!.TypeId)
            .Select(g => g.First())
            .ToList();

        if (unidentifiedTypes.Count == 0)
        {
            // No unidentified items in inventory — scroll consumed but no effect.
            events.Add(new Core.SpellEvent
            {
                ActorId   = caster.Id,
                SpellId   = spell.SpellId,
                SpellName = "Identify",
                Success   = true,
                StatusApplied = "none",
            });
            return events;
        }

        // Randomly identify 1-3 types.
        int count = Math.Min(unidentifiedTypes.Count, state.Rng.Next(1, 4));
        // Shuffle the candidates so selection is random.
        for (int i = unidentifiedTypes.Count - 1; i > 0; i--)
        {
            int j = state.Rng.Next(i + 1);
            (unidentifiedTypes[i], unidentifiedTypes[j]) = (unidentifiedTypes[j], unidentifiedTypes[i]);
        }

        for (int i = 0; i < count; i++)
        {
            var (_, tag, idComp) = unidentifiedTypes[i];
            bool newlyIdentified = registry.Identify(tag!.TypeId);
            if (newlyIdentified)
            {
                events.Add(new Core.IdentificationEvent
                {
                    ActorId        = caster.Id,
                    TypeId         = tag.TypeId,
                    IdentifiedName = idComp!.IdentifiedName,
                    Trigger        = "scroll_of_identify",
                });
            }
        }

        events.Add(new Core.SpellEvent
        {
            ActorId   = caster.Id,
            SpellId   = spell.SpellId,
            SpellName = "Identify",
            Success   = true,
        });

        return events;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Paralysis Potion — random 3-5 turn duration (PoC: ImmobilizedEffect, 3-5t random)
    // Used for both drink (self, targetEntityId=null) and throw (target, targetEntityId set).
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveParalysisPotion(
        Entity caster, SpellEffect spell, GameState state, int? targetEntityId)
    {
        // Random 3-5 turn duration as per PoC.
        int duration = state.Rng.Next(3, 6); // 3, 4, or 5

        if (targetEntityId == null)
        {
            // Drink — applies to caster (self). FreeActionTag blocks ImmobilizedEffect.
            var applied = StatusEffectProcessor.ApplyEffect<ImmobilizedEffect>(caster, duration);
            bool blocked = applied == null;
            return [new SpellEvent
            {
                ActorId = caster.Id,
                SpellId = spell.SpellId,
                SpellName = "paralysis",
                Success = !blocked,
                StatusApplied = blocked ? "" : "immobilized",
                StatusDuration = blocked ? 0 : duration,
            }];
        }

        // Throw — applies to target entity. FreeActionTag blocks ImmobilizedEffect.
        var target = FindTargetById(state, targetEntityId, spell.Range > 0 ? spell.Range : 10, caster);
        if (target == null)
        {
            return [new SpellEvent
            {
                ActorId = caster.Id, SpellId = spell.SpellId, SpellName = "paralysis", Success = false,
            }];
        }

        var throwApplied = StatusEffectProcessor.ApplyEffect<ImmobilizedEffect>(target, duration);
        bool throwBlocked = throwApplied == null;
        return [new SpellEvent
        {
            ActorId = caster.Id,
            SpellId = spell.SpellId,
            SpellName = "paralysis",
            TargetId = target.Id,
            AffectedIds = [target.Id],
            Success = !throwBlocked,
            StatusApplied = throwBlocked ? "" : "immobilized",
            StatusDuration = throwBlocked ? 0 : duration,
        }];
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Dispel (Hollowmark's Spell-Break) — removes one IStatusEffect from target within range.
    // Priority: PossessionEffect first (dispatches to PossessionSystem.OnPossessionDispelled),
    // then the first other effect found. Range default: 5 tiles (Chebyshev), LOS required.
    // Variant 3 path: WardenInitiated PossessionEffect on a past-Sasha corpse collapses the host.
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveDispel(
        Entity caster, SpellEffect spell, GameState state, int? targetEntityId)
    {
        var events = new List<TurnEvent>();

        if (targetEntityId == null)
        {
            events.Add(FailEvent(caster, spell, "Spell-Break"));
            return events;
        }

        var target = state.Monsters.FirstOrDefault(m => m.Id == targetEntityId.Value);
        if (target == null)
        {
            events.Add(FailEvent(caster, spell, "Spell-Break"));
            return events;
        }

        int range = spell.Range > 0 ? spell.Range : 5;
        if (Core.PossessionSystem.ChebyshevDistance(caster.X, caster.Y, target.X, target.Y) > range
            || !state.Map.HasLineOfSight(caster.X, caster.Y, target.X, target.Y))
        {
            events.Add(FailEvent(caster, spell, "Spell-Break"));
            return events;
        }

        // PossessionEffect has highest priority — routes to dedicated dispel pipeline.
        var possEffect = target.Get<PossessionEffect>();
        if (possEffect != null)
        {
            PossessionSystem.OnPossessionDispelled(target, possEffect, state, events);
            events.Add(new SpellEvent { ActorId = caster.Id, SpellId = spell.SpellId, SpellName = "Spell-Break", Success = true });
            return events;
        }

        // Remove the first other status effect found on the target.
        var anyEffect = target.GetAllComponents().OfType<IStatusEffect>().FirstOrDefault();
        if (anyEffect == null)
        {
            events.Add(FailEvent(caster, spell, "Spell-Break"));
            return events;
        }

        string effectName = anyEffect.EffectName;
        target.RemoveByType(anyEffect.GetType());
        events.Add(new StatusExpiredEvent
        {
            ActorId    = caster.Id,
            EntityId   = target.Id,
            EffectName = effectName,
            Reason     = "dispelled",
        });
        events.Add(new SpellEvent { ActorId = caster.Id, SpellId = spell.SpellId, SpellName = "Spell-Break", Success = true });
        return events;
    }

    private static SpellEvent FailEvent(Entity caster, SpellEffect spell, string spellName) =>
        new() { ActorId = caster.Id, SpellId = spell.SpellId, SpellName = spellName, Success = false };

    // ──────────────────────────────────────────────────────────────────────────
    // Antidote Potion — removes PlagueEffect from the caster immediately.
    // PoC: drink_antidote — cures plague, no other effect.
    // If no plague is present: still consumed (no error), no status event emitted.
    // ──────────────────────────────────────────────────────────────────────────

    private static List<TurnEvent> ResolveAntidote(Entity caster, SpellEffect spell, GameState state)
    {
        var events = new List<TurnEvent>();

        if (caster.Has<PlagueEffect>())
        {
            caster.Remove<PlagueEffect>();
            events.Add(new StatusExpiredEvent
            {
                ActorId    = caster.Id,
                EntityId   = caster.Id,
                EffectName = "plague",
                Reason     = "cured",
            });
        }

        events.Add(new SpellEvent
        {
            ActorId   = caster.Id,
            SpellId   = spell.SpellId,
            SpellName = "antidote",
            Success   = true,
        });

        return events;
    }
}
