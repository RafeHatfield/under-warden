using UnderWarden.Logic.Combat;
using YamlDotNet.Serialization;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Deserialized consumable item definition from YAML.
/// </summary>
public sealed class ConsumableDefinition
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "heal_amount")]
    public int HealAmount { get; set; }

    /// <summary>
    /// Turns the player must wait before using another potion of this type (0 = no cooldown).
    /// The cooldown is tracked on the player's Fighter (PotionCooldownRemaining) and decremented
    /// once per player turn. A non-zero value decouples quantity from balance — the player can
    /// hold 20 potions and the RATE (heal × cooldown) is the only balance-relevant variable.
    /// </summary>
    [YamlMember(Alias = "use_cooldown_turns")]
    public int UseCooldownTurns { get; set; }

    [YamlMember(Alias = "char")]
    public string Char { get; set; } = "!";

    [YamlMember(Alias = "color")]
    public int[] Color { get; set; } = [255, 255, 255];

    /// <summary>
    /// Item category. Potions are ItemCategory.Potion by default; override via YAML.
    /// </summary>
    [YamlMember(Alias = "category")]
    public ItemCategory Category { get; set; } = ItemCategory.Potion;

    /// <summary>
    /// Human-readable display name. Defaults to Name if not set.
    /// Used as the IdentifiedName in the IdentifiableItem component.
    /// </summary>
    public string DisplayName => Name ?? "";

    /// <summary>
    /// YAML key (e.g. "healing_potion"). Set by ContentLoader after deserialization.
    /// </summary>
    public string Id { get; set; } = "";

    // ── Spell-effect potion fields ────────────────────────────────────────────
    // Potions with a spell_id are routed through SpellResolver rather than TryHeal.
    // These fields mirror SpellDefinition but live here because potions are a
    // separate YAML section (consumables:) and have extra fields (is_potion, throw_spell_id).

    /// <summary>
    /// Canonical spell ID for the drink effect. Matches a handler in SpellResolver.
    /// Empty for healing_potion (which uses HealAmount directly via TryHeal).
    /// </summary>
    [YamlMember(Alias = "spell_id")]
    public string SpellId { get; set; } = "";

    /// <summary>Targeting mode string. "self" for all drink-only potions.</summary>
    [YamlMember(Alias = "targeting")]
    public string Targeting { get; set; } = "self";

    /// <summary>Status effect duration in turns. 0 = instant (e.g., antidote).</summary>
    [YamlMember(Alias = "duration")]
    public int Duration { get; set; }

    /// <summary>
    /// True for potions — bypasses the SilencedEffect gate in ResolveSpellAction.
    /// False for scrolls (the default). Set is_potion: true in YAML for all potions.
    /// </summary>
    [YamlMember(Alias = "is_potion")]
    public bool IsPotion { get; set; }

    /// <summary>
    /// Spell ID for the throw effect. When set, tapping the potion in inventory
    /// enters targeting mode (throw-first). Tapping self triggers the drink SpellId.
    /// Empty for drink-only potions.
    /// </summary>
    [YamlMember(Alias = "throw_spell_id")]
    public string ThrowSpellId { get; set; } = "";

    /// <summary>Max range in tiles for targeting mode. 0 = no limit.</summary>
    [YamlMember(Alias = "range")]
    public int Range { get; set; }

    /// <summary>Base damage value. 0 = not a damage potion.</summary>
    [YamlMember(Alias = "damage")]
    public int Damage { get; set; }

    /// <summary>Parse the targeting string to the TargetingMode enum. Defaults to Self.</summary>
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
