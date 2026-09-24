using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

public enum EquipmentSlot
{
    MainHand,
    OffHand,
    Head,
    Chest,
    Feet,
    LeftRing,
    RightRing,
    Neck,
    /// <summary>
    /// Holds special ammo (fire_arrow, net_arrow stacks). Only items with IsSpecialAmmo=true
    /// may be equipped here. Validated at equip-time in TurnController.ResolveEquip.
    /// Normal arrows are infinite and not tracked — no quiver item needed for basic ranged.
    /// </summary>
    Quiver,
}

/// <summary>
/// Makes an entity equippable in an equipment slot.
/// Weapons have damage ranges and to-hit bonuses.
/// Armor has AC bonuses and armor type (for DEX cap rules later).
/// </summary>
public sealed class Equippable : IComponent
{
    public Entity? Owner { get; set; }

    public EquipmentSlot Slot { get; }
    public int DamageMin { get; set; }
    public int DamageMax { get; set; }
    public int ToHitBonus { get; set; }
    public int ArmorClassBonus { get; set; }
    public string? DamageType { get; set; }
    public string? ArmorType { get; set; }
    public int CritThreshold { get; set; } = 20;

    /// <summary>
    /// Physical material of the weapon. "metal" weapons can be corroded by slimes.
    /// "wood" and null are immune. Matches PoC entities.yaml material field.
    /// </summary>
    public string? Material { get; set; }

    /// <summary>
    /// Original DamageMax at creation time — never changes.
    /// Used as the corrosion floor: weapon cannot be degraded below 50% of base.
    /// Call SetBaseDamageMax() once after DamageMax is set at creation.
    /// </summary>
    public int BaseDamageMax { get; private set; }

    /// <summary>Capture DamageMax as the baseline. Call once at item creation.</summary>
    public void SetBaseDamageMax() => BaseDamageMax = DamageMax;

    /// <summary>Restore the exact baseline after a mid-run load (it can diverge from DamageMax via
    /// corrosion, so it must persist independently). Serializer-only.</summary>
    public void RestoreBaseDamageMax(int baseDamageMax) => BaseDamageMax = baseDamageMax;

    public Equippable(EquipmentSlot slot)
    {
        Slot = slot;
    }

    /// <summary>
    /// True for bows and crossbows. Detection by explicit flag only — spears and thrown weapons
    /// are melee and do NOT set this. RangedCombatService gates the ranged resolution path on this.
    /// </summary>
    public bool IsRangedWeapon { get; set; }

    /// <summary>
    /// Two-handed weapons (bows) clear the OffHand slot when equipped.
    /// This prevents bow + shield stacking. Equipping a two-handed weapon returns the
    /// previous off-hand item to inventory before placing the weapon in MainHand.
    /// </summary>
    public bool TwoHanded { get; set; }

    /// <summary>
    /// True for quiver ammo types (fire_arrow, net_arrow). Validated at equip-time:
    /// only items with IsSpecialAmmo=true can occupy EquipmentSlot.Quiver.
    /// Consumed on hit OR miss; NOT on out-of-range denial or if player dies to retaliation.
    /// </summary>
    public bool IsSpecialAmmo { get; set; }

    /// <summary>Roll weapon damage. Returns 0 if no damage range (armor, etc).</summary>
    public int RollDamage(SeededRandom rng)
    {
        return CombatMath.RollDamage(rng, DamageMin, DamageMax);
    }

    public bool IsWeapon => DamageMin > 0 || DamageMax > 0;
}
