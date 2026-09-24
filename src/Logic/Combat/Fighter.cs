using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

/// <summary>
/// Combat stats component. Anything that can fight or be fought has this.
/// Holds base stats — equipment and status modifiers are applied by services during resolution.
/// </summary>
public sealed class Fighter : IComponent
{
    public Entity? Owner { get; set; }

    // Health
    public int BaseMaxHp { get; }
    public int Hp { get; set; }

    // D&D-style ability scores (8-18 range, default 10)
    public int Strength { get; set; }
    public int Dexterity { get; set; }
    public int Constitution { get; set; }

    // Hit chance modifiers
    public int Accuracy { get; set; }
    public int Evasion { get; set; }

    // Natural damage range (fists/claws — used when no weapon equipped)
    public int DamageMin { get; set; }
    public int DamageMax { get; set; }

    /// <summary>
    /// Damage type for unarmed attacks (e.g. "acid" for slimes). Null for most creatures.
    /// CombatResolver uses this when the attacker has no weapon equipped.
    /// </summary>
    public string? NaturalDamageType { get; set; }

    // Legacy power/defense (kept for compatibility with YAML definitions)
    public int BasePower { get; set; }
    public int BaseDefense { get; set; }

    // Experience awarded on kill (monsters only)
    public int Xp { get; set; }

    /// <summary>
    /// Extra max HP from equipped constitution rings.
    /// Stacks with a second ring. Reversed on unequip (Hp clamped to new MaxHp).
    /// Kept separate from ConstitutionMod so it's explicitly reversible without
    /// affecting the CON stat cascade.
    /// </summary>
    public int RingMaxHpBonus { get; set; }

    /// <summary>
    /// Extra max HP from depth boons (fortitude_10, resilience_5).
    /// BaseMaxHp is read-only, so boon HP bonuses are tracked separately —
    /// same pattern as RingMaxHpBonus. Carried forward by PlayerCarryForward.
    /// </summary>
    public int BoonMaxHpBonus { get; set; }

    /// <summary>
    /// Turns remaining until the player can use another cooldown-gated potion (0 = ready).
    /// Decremented once per player turn (in TurnController.ProcessTurnEnd). Set to
    /// Consumable.UseCooldownTurns on a successful use. Irrelevant between fights (10+ travel
    /// turns reset it), so the between-fight reset is preserved. Carried by PlayerCarryForward.
    /// </summary>
    public int PotionCooldownRemaining { get; set; }

    public Fighter(
        int hp,
        int defense = 0,
        int power = 0,
        int xp = 0,
        int damageMin = 0,
        int damageMax = 0,
        int strength = 10,
        int dexterity = 10,
        int constitution = 10,
        int accuracy = HitModel.DefaultAccuracy,
        int evasion = HitModel.DefaultEvasion)
    {
        BaseMaxHp = hp;
        Hp = hp;
        BaseDefense = defense;
        BasePower = power;
        Xp = xp;
        DamageMin = damageMin;
        DamageMax = damageMax;
        Strength = strength;
        Dexterity = dexterity;
        Constitution = constitution;
        Accuracy = accuracy;
        Evasion = evasion;
    }

    /// <summary>
    /// Max HP = base + CON modifier + ring bonus. Matches PoC fighter.max_hp: base_max_hp + constitution_mod.
    /// RingMaxHpBonus is the +20 per equipped ring_of_constitution (stacks, cleanly reversible).
    /// Equipment bonuses applied externally by services.
    /// Note: Hp starts at BaseMaxHp (flat), so player starts slightly below max HP if CON > 10.
    /// </summary>
    public int MaxHp => BaseMaxHp + ConstitutionMod + RingMaxHpBonus + BoonMaxHpBonus;

    /// <summary>Strength modifier from ability score.</summary>
    public int StrengthMod => CombatMath.StatModifier(Strength);

    /// <summary>Dexterity modifier from ability score.</summary>
    public int DexterityMod => CombatMath.StatModifier(Dexterity);

    /// <summary>Constitution modifier from ability score.</summary>
    public int ConstitutionMod => CombatMath.StatModifier(Constitution);

    /// <summary>
    /// Base armor class: 10 + dexterity modifier. Equipment and status effects add to this externally.
    /// </summary>
    public int BaseArmorClass => 10 + DexterityMod;

    public bool IsAlive => Hp > 0;

    /// <summary>
    /// When true, this entity can open closed doors during movement.
    /// Set from MonsterDefinition.CanOpenDoors at spawn time (via MonsterFactory).
    /// Always true for the player.
    /// </summary>
    public bool CanOpenDoors { get; set; }

    /// <summary>
    /// When true, the next player attack on this entity is a guaranteed hit (surprise attack).
    /// PoC: is_monster_aware() starts False for all monsters, even in "aware" scenario state.
    /// First player attack on any monster triggers one guaranteed hit, then awareness is set.
    /// Defaults to true for all entities; TurnController consumes it on first player attack.
    /// </summary>
    public bool SurpriseAttackAvailable { get; private set; } = true;

    /// <summary>Consume the surprise attack advantage. Called before first player attack lands.</summary>
    public void ConsumeSurpriseAttack() => SurpriseAttackAvailable = false;

    /// <summary>
    /// Apply damage. Returns actual damage dealt (after floor of 0).
    /// Does not handle resistances — that's the combat resolver's job.
    /// </summary>
    public int TakeDamage(int amount)
    {
        int actual = Math.Max(0, amount);
        Hp -= actual;
        return actual;
    }

    /// <summary>Restore HP, capped at MaxHp. Returns actual amount healed.</summary>
    public int Heal(int amount)
    {
        if (amount <= 0) return 0;
        int maxHp = MaxHp;
        int before = Hp;
        Hp = Math.Min(Hp + amount, maxHp);
        return Hp - before;
    }
}
