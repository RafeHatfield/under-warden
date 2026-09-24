using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Knowledge;

/// <summary>
/// Pure data record for the item inspect UI.
/// Built from an Entity's components — all Godot-free so it can be populated and tested in logic layer.
/// </summary>
public sealed record ItemInspectView(
    string Name,
    string Category,
    IReadOnlyList<string> StatLines
)
{
    /// <summary>
    /// Build an ItemInspectView from an item entity by reading its components.
    /// Returns a view with generic "Unknown Item" if no recognized components are found.
    ///
    /// Potions/scrolls/wands/rings (anything with IdentifiableItem) show only the mystery
    /// appearance name and a placeholder line until identified — mirrors ItemDisplay.GetDisplayName's
    /// gating so the inspect popup can't spoil identification. Weapons and plain armor have no
    /// IdentifiableItem component and are always shown in full, per docs/systems/LOOT_AND_IDENTIFICATION.md.
    /// Pass registry/pool null (e.g. scenario/harness mode) to always show true stats.
    /// </summary>
    public static ItemInspectView From(Entity item, IdentificationRegistry? registry = null, AppearancePool? pool = null)
    {
        var idComp = item.Get<IdentifiableItem>();
        if (idComp != null && !IsIdentified(item, registry))
        {
            string mysteryName = ItemDisplay.GetDisplayName(item, registry, pool);
            return new ItemInspectView(mysteryName, CategoryOf(item), new[] { "Unidentified — use it to reveal its effect" });
        }

        var lines = new List<string>();
        string category = CategoryOf(item);

        var equippable = item.Get<Equippable>();
        var consumable = item.Get<Consumable>();
        var spellEffect = item.Get<SpellEffect>();
        var wand = item.Get<WandComponent>();

        if (equippable != null)
        {
            bool isWeapon = equippable.Slot == EquipmentSlot.MainHand || equippable.Slot == EquipmentSlot.OffHand;

            if (isWeapon)
            {
                if (equippable.DamageMin > 0 || equippable.DamageMax > 0)
                    lines.Add($"Damage: {equippable.DamageMin}-{equippable.DamageMax}");
                if (equippable.ToHitBonus != 0)
                    lines.Add($"Accuracy: +{equippable.ToHitBonus}");
                if (!string.IsNullOrEmpty(equippable.DamageType))
                    lines.Add($"Type: {equippable.DamageType}");
            }
            else if (equippable.ArmorClassBonus != 0)
            {
                lines.Add($"AC Bonus: +{equippable.ArmorClassBonus}");
            }
        }
        else if (wand != null && spellEffect != null)
        {
            if (!string.IsNullOrEmpty(spellEffect.SpellId))
                lines.Add($"Spell: {FormatSpellId(spellEffect.SpellId)}");

            string chargesText = wand.Infinite ? "∞" : $"{wand.Charges}/{wand.MaxCharges}";
            lines.Add($"Charges: {chargesText}");
        }
        else if (spellEffect != null && consumable != null)
        {
            if (!string.IsNullOrEmpty(spellEffect.SpellId))
                lines.Add($"Spell: {FormatSpellId(spellEffect.SpellId)}");
        }
        else if (consumable != null)
        {
            if (consumable.HealAmount > 0)
                lines.Add($"Heals: {consumable.HealAmount} HP");
            else
                lines.Add("Effect: Unknown");
        }

        return new ItemInspectView(item.Name, category, lines);
    }

    /// <summary>Whether the item is identified — true for anything without IdentifiableItem (weapons/plain armor), or when registry is null (scenario/harness mode).</summary>
    private static bool IsIdentified(Entity item, IdentificationRegistry? registry)
    {
        if (registry == null) return true;
        var tag = item.Get<ItemTag>();
        return tag == null || registry.IsIdentified(tag.TypeId);
    }

    /// <summary>Broad item category — safe to reveal even when unidentified (matches what the sprite/shape already implies).</summary>
    private static string CategoryOf(Entity item)
    {
        var equippable = item.Get<Equippable>();
        if (equippable != null)
        {
            bool isWeapon = equippable.Slot == EquipmentSlot.MainHand || equippable.Slot == EquipmentSlot.OffHand;
            if (isWeapon) return "Weapon";

            return equippable.Slot switch
            {
                EquipmentSlot.Head  => "Helmet",
                EquipmentSlot.Chest => "Armor",
                EquipmentSlot.Feet  => "Boots",
                EquipmentSlot.LeftRing or EquipmentSlot.RightRing => "Ring",
                EquipmentSlot.Neck  => "Amulet",
                _                   => "Armor",
            };
        }

        var wand = item.Get<WandComponent>();
        var spellEffect = item.Get<SpellEffect>();
        var consumable = item.Get<Consumable>();

        // Sasha's Sunder (ruling 2026-07-21): a reflavored keepsake — never label it a "wand", even
        // though it keeps wand-shaped mechanics. Scoped to this one narrative item; every other wand
        // still reads "Wand". This is the only player-facing "wand" string this item would otherwise hit.
        if (item.Get<ItemTag>()?.TypeId == "wand_of_spell_break") return "Keepsake";

        if (wand != null && spellEffect != null) return "Wand";
        if (spellEffect != null && consumable != null) return "Scroll";
        if (consumable != null) return "Potion";

        return "Item";
    }

    /// <summary>
    /// Format a spell ID for human-readable display.
    /// "magic_mapping" → "Magic Mapping", "lightning" → "Lightning".
    /// </summary>
    private static string FormatSpellId(string spellId)
    {
        if (string.IsNullOrEmpty(spellId)) return spellId;

        // Replace underscores with spaces and title-case each word
        var parts = spellId.Split('_');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
                parts[i] = char.ToUpper(parts[i][0]) + parts[i][1..];
        }
        return string.Join(" ", parts);
    }
}
