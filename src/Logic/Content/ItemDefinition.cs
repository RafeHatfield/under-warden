using YamlDotNet.Serialization;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Item category — determines identification behaviour at runtime.
/// Weapons/armor are always identified (Other). Consumables with secrets start hidden.
/// </summary>
public enum ItemCategory
{
    Other,
    Potion,
    Scroll,
    Wand,
    Ring,
}

/// <summary>
/// Deserialized item (weapon/armor) definition from YAML.
/// </summary>
public sealed class ItemDefinition
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "slot")]
    public string Slot { get; set; } = "main_hand";

    [YamlMember(Alias = "damage_min")]
    public int DamageMin { get; set; }

    [YamlMember(Alias = "damage_max")]
    public int DamageMax { get; set; }

    [YamlMember(Alias = "to_hit_bonus")]
    public int ToHitBonus { get; set; }

    [YamlMember(Alias = "armor_class_bonus")]
    public int ArmorClassBonus { get; set; }

    [YamlMember(Alias = "damage_type")]
    public string? DamageType { get; set; }

    [YamlMember(Alias = "armor_type")]
    public string? ArmorType { get; set; }

    [YamlMember(Alias = "crit_threshold")]
    public int CritThreshold { get; set; } = 20;

    [YamlMember(Alias = "speed_bonus")]
    public double SpeedBonus { get; set; }

    [YamlMember(Alias = "char")]
    public string Char { get; set; } = "?";

    [YamlMember(Alias = "color")]
    public int[] Color { get; set; } = [255, 255, 255];

    /// <summary>
    /// Physical material. "metal" weapons can be corroded by slimes.
    /// "wood" and null are immune.
    /// </summary>
    [YamlMember(Alias = "material")]
    public string? Material { get; set; }

    /// <summary>
    /// Item category. Weapons/armor default to Other (always identified).
    /// Potions/scrolls/wands/rings start hidden and need identification.
    /// </summary>
    [YamlMember(Alias = "category")]
    public ItemCategory Category { get; set; } = ItemCategory.Other;

    /// <summary>
    /// Ring effect type string (e.g. "protection", "speed"). Null for non-ring items.
    /// Set by ContentLoader.LoadRings when converting RingDefinition to ItemDefinition.
    /// Read by ItemFactory to attach a RingEffectComponent.
    /// </summary>
    [YamlMember(Alias = "ring_effect")]
    public string? RingEffect { get; set; }

    /// <summary>
    /// Ring effect strength (e.g. 2 for +2 AC, 20 for 20% teleport chance).
    /// Only meaningful when RingEffect is non-null.
    /// </summary>
    [YamlMember(Alias = "effect_strength")]
    public int EffectStrength { get; set; }

    /// <summary>
    /// Speed bonus ratio for speed rings (e.g. 0.10 for ring_of_speed, 0.25 for ring_of_hummingbird).
    /// Stored as a double here because the YAML integer EffectStrength cannot carry sub-integer precision.
    /// Set by ContentLoader.LoadRings based on ring_effect type.
    /// </summary>
    public double RingSpeedRatio { get; set; }

    /// <summary>
    /// Human-readable display name. Defaults to Name if not set.
    /// Used as the IdentifiedName in the IdentifiableItem component.
    /// </summary>
    public string DisplayName => Name ?? "";

    // ── Ranged Weapon / Ammo Fields (Phase 22.2) ─────────────────────────────

    /// <summary>
    /// True for bows and crossbows. Activates RangedCombatService resolution path.
    /// Spears and thrown weapons are melee — do NOT set this.
    /// </summary>
    [YamlMember(Alias = "is_ranged_weapon")]
    public bool IsRangedWeapon { get; set; }

    /// <summary>
    /// Two-handed weapons clear the OffHand slot when equipped.
    /// Both shortbow and longbow are two-handed — bow + shield is not allowed.
    /// </summary>
    [YamlMember(Alias = "two_handed")]
    public bool TwoHanded { get; set; }

    /// <summary>
    /// True for quiver ammo types (fire_arrow, net_arrow).
    /// Only items with IsSpecialAmmo=true can be equipped in EquipmentSlot.Quiver.
    /// </summary>
    [YamlMember(Alias = "is_special_ammo")]
    public bool IsSpecialAmmo { get; set; }

    /// <summary>
    /// Starting stack size for ammo items. fire_arrow=10, net_arrow=8.
    /// Ignored for non-ammo items.
    /// </summary>
    [YamlMember(Alias = "stack_size")]
    public int StackSize { get; set; }
}
