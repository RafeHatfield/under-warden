using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

/// <summary>
/// Single source of truth for ranged attack resolution (Phase 22.2).
/// Player-only gate — monsters do not use this code path in this release.
///
/// Resolution order for d≤1 (adjacent + retaliation):
///   1. Validity: LoS + range band. Denied → emit event + return.
///   2. Roll to-hit (computed now, resolved later).
///   3. Retaliation: if d≤1 and can_retaliate, defender free-melee vs player (armor halved).
///      If player dies → return (no shot, no ammo consumed).
///   4. Apply shot: if hit, apply band damage modifier + special ammo rider on-hit.
///   5. Consume special ammo (hit OR miss, not on denial, not if player died in step 3).
///   6. Knockback roll (10%, on hit only).
///
/// PoC reference: services/ranged_combat_service.py
/// </summary>
public static class RangedCombatService
{
    // ── Range Band Constants ──────────────────────────────────────────────────

    private const int OptimalMax = 6;       // max tile distance for full damage
    private const int MaxRange = 8;         // d > 8 is denied entirely
    private const double KnockbackChance = 0.10;

    // ── Public Entry Point ────────────────────────────────────────────────────

    /// <summary>
    /// Attempt a ranged attack from the player toward the target.
    /// Validates LoS and range band, then resolves the full sequence per the plan spec.
    /// Mutates state (HP, status effects, quiver) and appends events.
    ///
    /// state: current game state (for RNG, map, entity position lookup)
    /// events: list to append TurnEvents to (RangedAttackEvent, SpecialAmmoConsumedEvent, etc.)
    /// monsterFactory: optional — required for split spawning on kill, ignored here for now.
    /// </summary>
    public static void AttemptRangedAttack(
        Entity player,
        Entity target,
        GameState state,
        List<TurnEvent> events)
    {
        var rng = state.Rng;
        var playerFighter = player.Require<Fighter>();
        var targetFighter = target.Get<Fighter>();

        // Target must be alive and have a fighter component.
        if (targetFighter == null || !targetFighter.IsAlive) return;

        // ── Step 1: Range + LoS validity ─────────────────────────────────────

        int distance = player.ChebyshevDistanceTo(target.X, target.Y);
        var band = CalculateBand(distance);

        // LoS check: treat LoS failure same as denial — no attack, no ammo.
        bool hasLoS = state.Map.HasLineOfSight(player.X, player.Y, target.X, target.Y);

        if (band.Denied || !hasLoS)
        {
            events.Add(new RangedAttackEvent
            {
                ActorId   = player.Id,
                TargetId  = target.Id,
                Distance  = distance,
                BandName  = band.Name,
                Denied    = true,
            });
            return;
        }

        // ── Step 2: Roll to-hit (d20 + bonuses) ──────────────────────────────

        var playerEquip = player.Get<Equipment>();
        var weapon = playerEquip?.MainHand?.Get<Equippable>();

        int toHitBonus = playerFighter.DexterityMod + (playerEquip?.TotalToHitBonus ?? 0);
        // Apply status effect accuracy penalties (same as CombatResolver)
        if (player.Get<BlindedEffect>() is { } blindedFx) toHitBonus -= blindedFx.AccuracyPenalty;
        if (player.Get<FocusedEffect>() is { } focusedFx) toHitBonus += focusedFx.AccuracyBonus;
        if (player.Get<CrippledEffect>() is { } crippledFx) toHitBonus -= crippledFx.ToHitPenalty;
        if (player.Get<RallyEffect>() is { } rallyFx) toHitBonus += rallyFx.ToHitBonus;
        if (player.Get<HeroismEffect>() is { } heroismFx) toHitBonus += heroismFx.AttackBonus;

        int d20 = rng.Next(1, 21);
        int attackRoll = d20 + toHitBonus;

        // Target AC for the ranged shot
        var defEquip = target.Get<Equipment>();
        int targetAc = targetFighter.BaseArmorClass + (defEquip?.TotalArmorClassBonus ?? 0);
        if (target.Get<ShieldEffect>() is { } shieldFx) targetAc += shieldFx.AcBonus;
        if (target.Get<ProtectionEffect>() is { } protFx) targetAc += protFx.AcBonus;
        if (target.Get<BarkskinEffect>() is { } barkFx) targetAc += barkFx.AcBonus;
        if (target.Get<CrippledEffect>() is { } defCrippledFx) targetAc -= defCrippledFx.AcPenalty;

        int critThreshold = weapon?.CritThreshold ?? 20;
        bool isCritical = d20 >= critThreshold;
        bool isFumble = d20 == 1;

        bool hit;
        if (isCritical) hit = true;
        else if (isFumble) hit = false;
        else hit = attackRoll >= targetAc;

        // ── Step 3: Retaliation (d≤1 only) ───────────────────────────────────

        bool retaliationTriggered = false;
        if (band.Retaliation && CanRetaliate(target))
        {
            retaliationTriggered = true;
            ResolveRetaliation(target, player, state, events);

            // If player died from retaliation, abort — no shot, no ammo consumed.
            if (!playerFighter.IsAlive)
            {
                events.Add(new RangedAttackEvent
                {
                    ActorId              = player.Id,
                    TargetId             = target.Id,
                    Distance             = distance,
                    BandName             = band.Name,
                    Denied               = false,
                    Hit                  = false,
                    Damage               = 0,
                    RetaliationTriggered = true,
                    KnockbackApplied     = false,
                });
                return;
            }
        }

        // ── Step 4: Apply shot ────────────────────────────────────────────────

        int damageBeforePenalty = 0;
        int damageFinal = 0;
        bool specialEffectApplied = false;

        if (hit && targetFighter.IsAlive)
        {
            // Roll base weapon damage (or player natural damage if no weapon)
            int baseDmg;
            if (weapon != null && weapon.IsWeapon)
                baseDmg = weapon.RollDamage(rng);
            else
                baseDmg = CombatMath.RollDamage(rng, playerFighter.DamageMin, playerFighter.DamageMax);

            baseDmg += playerFighter.StrengthMod;
            if (isCritical) baseDmg *= 2;
            if (player.Get<WeaknessEffect>() is { } wkFx) baseDmg -= wkFx.DamagePenalty;
            if (player.Get<RallyEffect>() is { } atkRallyFx) baseDmg += atkRallyFx.DamageBonus;
            if (player.Get<HeroismEffect>() is { } atkHeroFx) baseDmg += atkHeroFx.DamageBonus;
            damageBeforePenalty = Math.Max(1, baseDmg);

            // Apply range band multiplier (minimum 1 after)
            int modified = (int)(damageBeforePenalty * band.Multiplier);
            damageFinal = Math.Max(1, modified);

            targetFighter.TakeDamage(damageFinal);

            // Special ammo on-hit rider
            var quiverItem = playerEquip?.Quiver;
            var quiverEq = quiverItem?.Get<Equippable>();
            if (quiverEq != null && quiverEq.IsSpecialAmmo)
            {
                specialEffectApplied = ApplySpecialAmmoOnHit(quiverEq, target, state, events, player.Id);
            }

            // Wake sleeping targets on attack damage (same rule as melee)
            StatusEffectProcessor.OnDamageTaken(target, events);
        }

        // ── Step 5: Consume special ammo (hit OR miss, not denial, not dead player) ──

        bool knockbackApplied = false;
        {
            var quiverItem = playerEquip?.Quiver;
            var quiverEq = quiverItem?.Get<Equippable>();
            if (quiverItem != null && quiverEq != null && quiverEq.IsSpecialAmmo)
            {
                var consumable = quiverItem.Get<Consumable>();
                if (consumable != null)
                {
                    consumable.StackSize--;
                    int remaining = consumable.StackSize;

                    events.Add(new SpecialAmmoConsumedEvent
                    {
                        ActorId  = player.Id,
                        AmmoType = quiverEq.DamageType ?? quiverItem.Name,
                        Remaining = remaining,
                    });

                    // Auto-unequip quiver when exhausted
                    if (remaining <= 0)
                    {
                        playerEquip!.SetSlot(EquipmentSlot.Quiver, null);
                        // Return exhausted item to inventory if possible; drop on floor otherwise.
                        var inv = player.Get<Inventory>();
                        if (inv != null && !inv.Add(quiverItem))
                        {
                            quiverItem.X = player.X;
                            quiverItem.Y = player.Y;
                            state.FloorItems.Add(quiverItem);
                            state.Map.RegisterEntity(quiverItem);
                        }
                    }
                }
            }
        }

        // ── Step 6: Knockback roll (10% on hit only) ─────────────────────────

        if (hit && targetFighter.IsAlive)
        {
            if (rng.NextDouble() < KnockbackChance)
            {
                int tilesMoved = KnockbackService.TryKnockBackOneTile(player, target, state.Map, state);
                if (tilesMoved > 0)
                {
                    knockbackApplied = true;
                    int dx = Math.Sign(target.X - player.X);
                    int dy = Math.Sign(target.Y - player.Y);
                    events.Add(new RangedKnockbackEvent
                    {
                        ActorId    = player.Id,
                        TargetId   = target.Id,
                        Direction  = (dx, dy),
                        TilesMoved = tilesMoved,
                    });
                }
            }
        }

        // ── Emit summary event ────────────────────────────────────────────────

        bool targetKilled = hit && !targetFighter.IsAlive;
        events.Add(new RangedAttackEvent
        {
            ActorId              = player.Id,
            TargetId             = target.Id,
            Distance             = distance,
            BandName             = band.Name,
            Denied               = false,
            Hit                  = hit,
            Damage               = damageFinal,
            DamageBeforePenalty  = damageBeforePenalty,
            RetaliationTriggered = retaliationTriggered,
            KnockbackApplied     = knockbackApplied,
            SpecialEffectApplied = specialEffectApplied,
            TargetKilled         = targetKilled,
        });

        // ── Handle target death ───────────────────────────────────────────────
        // (mirrors TurnController.ResolvePlayerAttack death path)
        if (hit && !targetFighter.IsAlive)
        {
            events.Add(new DeathEvent { ActorId = target.Id, KillerId = player.Id });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the defender can make a retaliation strike.
    /// Blocked by: SleepEffect, ImmobilizedEffect (covers paralysis equivalents in C#).
    /// EntangledEffect does NOT block retaliation — entangled entities can still swing.
    /// Dead defenders cannot retaliate.
    /// </summary>
    public static bool CanRetaliate(Entity defender)
    {
        var fighter = defender.Get<Fighter>();
        if (fighter == null || !fighter.IsAlive) return false;

        // Incapacitating effects that prevent the entity from acting
        if (defender.Has<SleepEffect>()) return false;
        if (defender.Has<ImmobilizedEffect>()) return false;

        return true;
    }

    /// <summary>
    /// The defender makes a free melee attack against the player with player armor halved.
    /// The armor halving is applied by computing a custom effective AC during the check.
    /// We resolve this manually rather than routing through CombatResolver to avoid
    /// modifying Fighter state (no temporary stat mutation).
    /// </summary>
    private static void ResolveRetaliation(Entity retaliator, Entity player, GameState state, List<TurnEvent> events)
    {
        var retFighter = retaliator.Require<Fighter>();
        var playerFighter = player.Require<Fighter>();
        var retEquip = retaliator.Get<Equipment>();
        var playerEquip = player.Get<Equipment>();

        // Defender's to-hit roll
        int toHit = retFighter.DexterityMod + (retEquip?.TotalToHitBonus ?? 0);
        if (retaliator.Get<BlindedEffect>() is { } bFx) toHit -= bFx.AccuracyPenalty;
        if (retaliator.Get<CrippledEffect>() is { } cFx) toHit -= cFx.ToHitPenalty;

        int d20 = state.Rng.Next(1, 21);
        bool isCrit = d20 == 20;
        bool isFumble = d20 == 1;

        // Player AC with armor HALVED (bows at point-blank = fumbling, not defensive stance)
        int baseAc = playerFighter.BaseArmorClass;
        int armorBonus = playerEquip?.TotalArmorClassBonus ?? 0;
        int halvedArmor = armorBonus / 2; // floor division
        int effectivePlayerAc = baseAc + halvedArmor;
        if (player.Get<ShieldEffect>() is { } sFx) effectivePlayerAc += sFx.AcBonus;
        if (player.Get<ProtectionEffect>() is { } pFx) effectivePlayerAc += pFx.AcBonus;
        if (player.Get<BarkskinEffect>() is { } bkFx) effectivePlayerAc += bkFx.AcBonus;

        int attackRoll = d20 + toHit;

        bool hit;
        if (isCrit) hit = true;
        else if (isFumble) hit = false;
        else hit = attackRoll >= effectivePlayerAc;

        int damage = 0;
        bool killed = false;
        if (hit)
        {
            var retWeapon = retEquip?.MainHand?.Get<Equippable>();
            int baseDmg;
            if (retWeapon != null && retWeapon.IsWeapon)
                baseDmg = retWeapon.RollDamage(state.Rng);
            else
                baseDmg = CombatMath.RollDamage(state.Rng, retFighter.DamageMin, retFighter.DamageMax);

            baseDmg += retFighter.StrengthMod;
            if (isCrit) baseDmg *= 2;
            damage = Math.Max(1, baseDmg);
            playerFighter.TakeDamage(damage);
            killed = !playerFighter.IsAlive;
        }

        events.Add(new AttackEvent
        {
            ActorId       = retaliator.Id,
            TargetId      = player.Id,
            Hit           = hit,
            Damage        = damage,
            IsCritical    = isCrit,
            IsFumble      = isFumble,
            TargetKilled  = killed,
            IsBonusAttack = false,
        });

        if (killed)
        {
            events.Add(new DeathEvent { ActorId = player.Id, KillerId = retaliator.Id });
        }
    }

    /// <summary>
    /// Apply special ammo on-hit effect (fire_arrow → BurningEffect, net_arrow → EntangledEffect).
    /// Effect determined by the ammo's DamageType field (used as the effect key).
    /// Returns true if the rider effect was successfully applied.
    /// </summary>
    private static bool ApplySpecialAmmoOnHit(
        Equippable quiverEq, Entity target, GameState state, List<TurnEvent> events, int playerId)
    {
        string? ammoType = quiverEq.DamageType;
        if (string.IsNullOrEmpty(ammoType)) return false;

        switch (ammoType)
        {
            case "fire":
                // fire_arrow: BurningEffect, flat 1 dmg/turn, 3 turns, 100% on hit
                var burning = StatusEffectProcessor.ApplyEffect<BurningEffect>(target, 3);
                if (burning != null)
                {
                    burning.DamagePerTurn = 1; // flat 1/turn — NOT 1d4 (PoC test is authoritative)
                    events.Add(new StatusAppliedEvent
                    {
                        ActorId    = playerId,
                        TargetId   = target.Id,
                        EffectName = "burning",
                        Duration   = burning.RemainingTurns,
                    });
                    return true;
                }
                return false;

            case "entangle":
                // net_arrow: EntangledEffect, 50% chance on hit, 1 turn
                if (state.Rng.NextDouble() < 0.50)
                {
                    var entangled = StatusEffectProcessor.ApplyEffect<EntangledEffect>(target, 1);
                    if (entangled != null)
                    {
                        events.Add(new StatusAppliedEvent
                        {
                            ActorId    = playerId,
                            TargetId   = target.Id,
                            EffectName = "entangled",
                            Duration   = entangled.RemainingTurns,
                        });
                        return true;
                    }
                }
                return false;

            default:
                return false;
        }
    }

    // ── Range Band Table ──────────────────────────────────────────────────────

    private readonly record struct RangeBand(string Name, double Multiplier, bool Denied, bool Retaliation);

    /// <summary>
    /// Single source of truth for the range band table.
    /// Matches plan spec exactly: d≤1=25%+retaliation, d=2=50%, d3-6=100%, d7=50%, d8=25%, d>8=denied.
    /// </summary>
    private static RangeBand CalculateBand(int distance)
    {
        if (distance > MaxRange)
            return new("denied_out_of_range", 0.0, Denied: true, Retaliation: false);
        if (distance == MaxRange) // d==8
            return new("extreme_range", 0.25, Denied: false, Retaliation: false);
        if (distance == OptimalMax + 1) // d==7
            return new("far_range", 0.50, Denied: false, Retaliation: false);
        if (distance >= 3) // d3-6
            return new("optimal_range", 1.0, Denied: false, Retaliation: false);
        if (distance == 2)
            return new("close_range", 0.50, Denied: false, Retaliation: false);
        // distance <= 1 (including d==0 edge case)
        return new("adjacent_threatened", 0.25, Denied: false, Retaliation: true);
    }
}
