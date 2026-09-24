using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

/// <summary>
/// Result of a single attack resolution.
/// </summary>
public sealed class AttackResult
{
    public bool Hit { get; init; }
    public bool IsCritical { get; init; }
    public bool IsFumble { get; init; }
    public int Damage { get; init; }
    public int D20Roll { get; init; }
    public bool TargetKilled { get; init; }
    public bool BonusAttackTriggered { get; init; }
}

/// <summary>
/// Resolves combat between entities. Single source of truth for attack resolution.
/// Stateless — all randomness comes from the injected SeededRandom.
/// </summary>
public static class CombatResolver
{
    /// <summary>
    /// Resolve a melee attack from attacker to defender.
    /// Uses d20 roll vs AC for hit determination, then rolls damage on hit.
    /// Natural 20 = critical (2x damage). Natural 1 = fumble (auto-miss).
    /// </summary>
    public static AttackResult ResolveAttack(Entity attacker, Entity defender, SeededRandom rng,
        int extraToHitBonus = 0, bool forceHit = false)
    {
        var atk = attacker.Require<Fighter>();
        var def = defender.Require<Fighter>();
        var atkEquip = attacker.Get<Equipment>();
        var defEquip = defender.Get<Equipment>();

        int d20 = rng.Next(1, 21); // 1-20 inclusive (consumed for RNG determinism even on forceHit)

        // To-hit: DEX mod + weapon to-hit bonus + status effect modifiers
        int toHitBonus = atk.DexterityMod + (atkEquip?.TotalToHitBonus ?? 0);

        // BlindedEffect: -4 accuracy penalty on the attacker.
        // FocusedEffect: +3 accuracy bonus on the attacker (buff).
        if (attacker.Get<BlindedEffect>() is { } blindedFx) toHitBonus -= blindedFx.AccuracyPenalty;
        if (attacker.Get<FocusedEffect>() is { } focusedFx) toHitBonus += focusedFx.AccuracyBonus;
        // CrippledEffect: -ToHitPenalty on the attacker.
        // RallyEffect: +ToHitBonus on the attacker (chieftain buff).
        // HeroismEffect: +AttackBonus on the attacker (potion buff).
        var attackerCrippled = attacker.Get<CrippledEffect>();
        var attackerRallied  = attacker.Get<RallyEffect>();
        var attackerHeroism  = attacker.Get<HeroismEffect>();
        if (attackerCrippled != null) toHitBonus -= attackerCrippled.ToHitPenalty;
        if (attackerRallied  != null) toHitBonus += attackerRallied.ToHitBonus;
        if (attackerHeroism  != null) toHitBonus += attackerHeroism.AttackBonus;

        // External bonus: Command the Dead (+1 for undead allies near a lich)
        toHitBonus += extraToHitBonus;

        int attackRoll = d20 + toHitBonus;

        // AC: base (10 + DEX mod) + armor AC bonus + active status effect bonuses (Phase 1).
        // Shield (+4), Protection (+3), and Barkskin (+4) are read at resolution time —
        // the base ArmorClass stat is never mutated by status effects.
        int targetAc = def.BaseArmorClass + (defEquip?.TotalArmorClassBonus ?? 0);
        if (defender.Get<ShieldEffect>() is { } shieldFx) targetAc += shieldFx.AcBonus;
        if (defender.Get<ProtectionEffect>() is { } protFx) targetAc += protFx.AcBonus;
        if (defender.Get<BarkskinEffect>() is { } barkFx) targetAc += barkFx.AcBonus;
        // CrippledEffect on defender: -AcPenalty (defender is easier to hit).
        var defenderCrippled = defender.Get<CrippledEffect>();
        if (defenderCrippled != null) targetAc -= defenderCrippled.AcPenalty;
        // ShieldWallComponent: skeleton formation bonus (cached by SkeletonAI each turn).
        if (defender.Get<ShieldWallComponent>() is { } sw && sw.CurrentAcBonus > 0)
            targetAc += sw.CurrentAcBonus;

        // Crit threshold from weapon (keen weapons crit on 19-20)
        int critThreshold = 20;
        var weapon = atkEquip?.MainHand?.Get<Equippable>();
        if (weapon != null && weapon.CritThreshold >= 1 && weapon.CritThreshold <= 20)
            critThreshold = weapon.CritThreshold;

        bool isCritical = !forceHit && d20 >= critThreshold;
        bool isFumble = !forceHit && d20 == 1;

        bool hit;
        if (forceHit) hit = true;           // Surprise attack: always hits, not a crit
        else if (isCritical) hit = true;
        else if (isFumble) hit = false;
        else hit = attackRoll >= targetAc;

        int damage = 0;
        bool killed = false;

        if (hit)
        {
            // Weapon damage if equipped, otherwise natural damage
            int baseDmg;
            if (weapon != null && weapon.IsWeapon)
                baseDmg = weapon.RollDamage(rng);
            else
                baseDmg = CombatMath.RollDamage(rng, atk.DamageMin, atk.DamageMax);

            damage = baseDmg + atk.StrengthMod; // PoC attack_d20: damage = base_damage + str_modifier

            if (isCritical)
                damage *= 2;

            // WeaknessEffect: -2 damage penalty on the attacker. Minimum 1 damage after.
            if (attacker.Get<WeaknessEffect>() is { } weaknessFx)
                damage -= weaknessFx.DamagePenalty;

            // RallyEffect: +DamageBonus on the attacker (chieftain buff).
            // HeroismEffect: +DamageBonus on the attacker (potion buff).
            if (attackerRallied != null) damage += attackerRallied.DamageBonus;
            if (attackerHeroism != null) damage += attackerHeroism.DamageBonus;

            // Apply damage type resistance/vulnerability.
            // Weapon damage type takes priority; fall back to attacker's natural damage type
            // (e.g. "acid" for slimes) when no weapon is equipped.
            string? dmgType = weapon?.DamageType ?? attacker.Get<Fighter>()?.NaturalDamageType;
            var modifiers = defender.Get<DamageModifiers>();
            if (modifiers != null && dmgType != null)
                damage = modifiers.ApplyTo(damage, dmgType);

            damage = Math.Max(1, damage); // minimum 1 damage on hit
            def.TakeDamage(damage);
            killed = !def.IsAlive;
        }

        // Check for bonus attack (momentum system)
        bool bonusTriggered = false;
        if (hit && !killed)
        {
            var tracker = attacker.Get<SpeedBonusTracker>();
            if (tracker != null && SpeedBonusTracker.CanBuildMomentum(attacker, defender))
                bonusTriggered = tracker.RollForBonusAttack(rng, defender);
        }

        return new AttackResult
        {
            Hit = hit,
            IsCritical = isCritical,
            IsFumble = isFumble,
            Damage = damage,
            D20Roll = d20,
            TargetKilled = killed,
            BonusAttackTriggered = bonusTriggered,
        };
    }
}
