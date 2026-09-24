using UnderWarden.Logic.Combat;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Creates item entities from ItemDefinitions.
/// </summary>
public sealed class ItemFactory
{
    private readonly Dictionary<string, ItemDefinition> _definitions;
    private readonly EntityFactory _entityFactory;

    public ItemFactory(Dictionary<string, ItemDefinition> definitions, EntityFactory entityFactory)
    {
        _definitions = definitions;
        _entityFactory = entityFactory;
    }

    /// <summary>All item definition IDs available in this factory.</summary>
    public IEnumerable<string> AvailableIds => _definitions.Keys;

    /// <summary>Look up an item definition by ID. Returns null if not found.</summary>
    public ItemDefinition? GetDefinition(string itemId) =>
        _definitions.TryGetValue(itemId, out var def) ? def : null;

    /// <summary>Create an item entity. Returns null if ID not found.</summary>
    public Entity? Create(string itemId)
    {
        if (!_definitions.TryGetValue(itemId, out var def))
            return null;

        var slot = ParseSlot(def.Slot);
        var entity = _entityFactory.Create(def.Name ?? itemId);

        // ItemTag carries the YAML type ID so sprite lookup doesn't have to infer it
        // from the display name (which breaks when name diverges from key).
        entity.Add(new ECS.ItemTag(itemId));

        var equippable = new Equippable(slot)
        {
            DamageMin = def.DamageMin,
            DamageMax = def.DamageMax,
            ToHitBonus = def.ToHitBonus,
            ArmorClassBonus = def.ArmorClassBonus,
            DamageType = def.DamageType,
            ArmorType = def.ArmorType,
            CritThreshold = def.CritThreshold,
            Material = def.Material,
            IsRangedWeapon = def.IsRangedWeapon,
            TwoHanded = def.TwoHanded,
            IsSpecialAmmo = def.IsSpecialAmmo,
        };
        equippable.SetBaseDamageMax(); // capture DamageMax as corrosion floor baseline
        entity.Add(equippable);

        // Special ammo: attach a Consumable component to track remaining shots.
        // The Consumable.StackSize field is used as the remaining-shots counter.
        // HealAmount=0 so IsHealing=false — BotBrain won't treat arrows as potions.
        if (def.IsSpecialAmmo && def.StackSize > 0)
        {
            entity.Add(new Consumable(healAmount: 0)
            {
                StackSize = def.StackSize,
            });
        }

        // Weapon speed bonus for momentum system
        if (def.SpeedBonus > 0)
            entity.Add(new SpeedBonusTracker { EquipmentRatio = def.SpeedBonus });

        // Ring-specific components
        if (def.Category == ItemCategory.Ring)
        {
            // Parse RingEffectKind from the YAML ring_effect string
            if (!string.IsNullOrEmpty(def.RingEffect))
            {
                var kind = ParseRingEffectKind(def.RingEffect);
                entity.Add(new RingEffectComponent(kind, def.EffectStrength, def.RingSpeedRatio));
            }

            // Rings start unidentified — IdentifiableItem component holds both display names.
            // UnidentifiedName is set later by AppearancePool/PreIdentification when the item
            // is placed on a floor. In scenario mode it stays empty (rings appear identified).
            entity.Add(new IdentifiableItem
            {
                IdentifiedName = def.DisplayName,
                UnidentifiedName = "",
            });
        }

        return entity;
    }

    /// <summary>
    /// Parse a ring_effect string from YAML to RingEffectKind.
    /// Unknown strings map to Luck (Phase 2) as a safe inert default rather than throwing.
    /// </summary>
    private static RingEffectKind ParseRingEffectKind(string effect) => effect switch
    {
        "protection"   => RingEffectKind.Protection,
        "strength"     => RingEffectKind.Strength,
        "dexterity"    => RingEffectKind.Dexterity,
        "constitution" => RingEffectKind.Constitution,
        "might"        => RingEffectKind.Might,
        "regeneration" => RingEffectKind.Regeneration,
        "speed"        => RingEffectKind.Speed,
        "hummingbird"  => RingEffectKind.Speed,   // hummingbird is a stronger speed ring
        "free_action"  => RingEffectKind.FreeAction,
        "teleportation"=> RingEffectKind.Teleportation,
        "resistance"   => RingEffectKind.Resistance,
        "clarity"      => RingEffectKind.Clarity,
        "invisibility" => RingEffectKind.Invisibility,
        "searching"    => RingEffectKind.Searching,
        "wizardry"     => RingEffectKind.Wizardry,
        "luck"         => RingEffectKind.Luck,
        _              => RingEffectKind.Luck,
    };

    private static EquipmentSlot ParseSlot(string slot) => slot switch
    {
        "main_hand"  => EquipmentSlot.MainHand,
        "off_hand"   => EquipmentSlot.OffHand,
        "head"       => EquipmentSlot.Head,
        "chest"      => EquipmentSlot.Chest,
        "feet"       => EquipmentSlot.Feet,
        "left_ring"  => EquipmentSlot.LeftRing,
        "right_ring" => EquipmentSlot.RightRing,
        "quiver"     => EquipmentSlot.Quiver,
        _            => EquipmentSlot.MainHand,
    };
}
