using UnderWarden.Logic.Combat;
using YamlDotNet.Serialization;

namespace UnderWarden.Logic.Content;

/// <summary>
/// YAML-deserialized definition for a scroll or wand.
///
/// Scrolls and wands share this definition type. The is_wand flag controls which
/// runtime component is applied: scrolls get Consumable+SpellEffect, wands get
/// WandComponent+SpellEffect.
///
/// YAML sections: scrolls: and wands: under entities.yaml (or a separate spells.yaml).
/// PoC reference: ~/development/rlike/spells/spell_definition.py
/// </summary>
public sealed class SpellDefinition
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "description")]
    public string Description { get; set; } = "";

    /// <summary>
    /// Canonical spell identifier matching a handler in SpellResolver.
    /// e.g. "lightning", "earthquake", "magic_mapping"
    /// </summary>
    [YamlMember(Alias = "spell_id")]
    public string SpellId { get; set; } = "";

    /// <summary>Targeting mode string: "self", "auto_closest", "aoe_self", "single_target", "location", "portal".</summary>
    [YamlMember(Alias = "targeting")]
    public string Targeting { get; set; } = "self";

    /// <summary>Base damage value. 0 = not a damage spell.</summary>
    [YamlMember(Alias = "damage")]
    public int Damage { get; set; }

    /// <summary>AoE radius in tiles. 0 = single target.</summary>
    [YamlMember(Alias = "radius")]
    public int Radius { get; set; }

    /// <summary>Max targeting range in tiles. 0 = no limit.</summary>
    [YamlMember(Alias = "range")]
    public int Range { get; set; }

    /// <summary>Status effect duration in turns. 0 = instant.</summary>
    [YamlMember(Alias = "duration")]
    public int Duration { get; set; }

    /// <summary>
    /// Probability (0.0–1.0) that the spell misfires and picks a random target instead.
    /// Only used by Teleport Scroll (0.10 = 10% chance). 0 = no misfire.
    /// </summary>
    [YamlMember(Alias = "misfire_chance")]
    public double MisfireChance { get; set; }

    // ── Visual representation ──────────────────────────────────────────────

    [YamlMember(Alias = "char")]
    public string Char { get; set; } = "~";

    [YamlMember(Alias = "color")]
    public int[] Color { get; set; } = [255, 255, 200];

    // ── Wand-specific fields (ignored for scrolls) ─────────────────────────

    /// <summary>True if this definition creates a wand (WandComponent) rather than a scroll (Consumable).</summary>
    [YamlMember(Alias = "is_wand")]
    public bool IsWand { get; set; }

    /// <summary>Starting charges for a wand. Randomly chosen from [min_charges, max_charges] on creation.</summary>
    [YamlMember(Alias = "min_charges")]
    public int MinCharges { get; set; } = 2;

    [YamlMember(Alias = "max_charges")]
    public int MaxCharges { get; set; } = 4;

    /// <summary>Hard cap on charges this wand can accumulate (via recharging).</summary>
    [YamlMember(Alias = "charge_cap")]
    public int ChargeCap { get; set; } = 10;

    /// <summary>True if this wand has unlimited uses (Wand of Portals).</summary>
    [YamlMember(Alias = "infinite")]
    public bool Infinite { get; set; }

    /// <summary>
    /// SpellId of the scroll that recharges this wand.
    /// When a player picks up a scroll with this spell_id and this wand has room,
    /// the scroll is consumed and +1 charge is added.
    /// </summary>
    [YamlMember(Alias = "recharge_scroll")]
    public string? RechargeScroll { get; set; }

    /// <summary>
    /// Item category. Scrolls default to ItemCategory.Scroll; wands to ItemCategory.Wand.
    /// Set by ContentLoader after deserialization based on the is_wand flag.
    /// </summary>
    [YamlMember(Alias = "category")]
    public ItemCategory Category { get; set; } = ItemCategory.Scroll;

    /// <summary>
    /// Human-readable display name. Defaults to Name if not set.
    /// Used as the IdentifiedName in the IdentifiableItem component.
    /// </summary>
    public string DisplayName => Name ?? "";

    /// <summary>
    /// YAML key (e.g. "scroll_of_lightning", "wand_of_fireball"). Set by ContentLoader after deserialization.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Parse the targeting string to the TargetingMode enum.
    /// Defaults to Self for unknown values.
    /// </summary>
    public TargetingMode ParseTargetingMode() => Targeting switch
    {
        "auto_closest"   => TargetingMode.AutoClosest,
        "aoe_self"       => TargetingMode.AoeSelf,
        "single_target"  => TargetingMode.SingleTarget,
        "location"       => TargetingMode.Location,
        "portal"         => TargetingMode.Portal,
        _                => TargetingMode.Self,
    };
}
