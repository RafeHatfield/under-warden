using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Presentation-layer helpers for displaying item names and sprites correctly based on
/// identification state.
///
/// These methods are the single point of truth for "what name/sprite does this item show?"
/// The logic is simple: check the registry, pick the correct string from IdentifiableItem
/// or the AppearancePool, and return it. No game rules live here — just display decisions.
///
/// Lives in Logic.Content (not Presentation) so it can be tested without Godot.
/// </summary>
public static class ItemDisplay
{
    /// <summary>
    /// Get the display name for an item based on its identification state.
    ///
    /// Returns:
    ///   - IdentifiedName  if the type is identified (or registry is null = scenario mode)
    ///   - UnidentifiedName from pool if not identified and pool+registry are present
    ///   - item.Name       as fallback if no IdentifiableItem component (weapons/armor)
    /// </summary>
    public static string GetDisplayName(Entity item,
        IdentificationRegistry? registry,
        AppearancePool? pool)
    {
        var tag    = item.Get<ItemTag>();
        var idComp = item.Get<IdentifiableItem>();

        // No identification component = weapon/armor = always show entity name
        if (tag == null || idComp == null)
            return item.Name;

        // No registry = scenario/harness mode = always identified
        if (registry == null)
            return idComp.IdentifiedName;

        if (registry.IsIdentified(tag.TypeId))
            return idComp.IdentifiedName;

        // Unidentified: get the mystery name from the pool, fall back to IdentifiedName
        if (pool != null)
        {
            var mystery = pool.GetDisplayName(tag.TypeId);
            if (!string.IsNullOrEmpty(mystery))
                return mystery;
        }

        // Fallback: if pool doesn't know this type (shouldn't happen), use entity name
        return item.Name;
    }

    /// <summary>
    /// Get the sprite type key to use for an item based on its identification state.
    ///
    /// For identified items (or items without IdentifiableItem): returns the TypeId.
    /// For unidentified items: returns the mystery sprite key from the AppearancePool.
    ///
    /// Callers pass this key to SpriteMapping.GetItemSpritePath().
    /// </summary>
    public static string GetSpriteKey(Entity item,
        IdentificationRegistry? registry,
        AppearancePool? pool)
    {
        var tag    = item.Get<ItemTag>();
        var idComp = item.Get<IdentifiableItem>();

        // No identification data = weapon/armor = use TypeId directly
        if (tag == null) return item.Name.ToLowerInvariant().Replace(' ', '_');
        if (idComp == null) return tag.TypeId;

        // No registry = scenario mode = always show true sprite
        if (registry == null || registry.IsIdentified(tag.TypeId))
            return tag.TypeId;

        // Unidentified: get mystery sprite key from pool
        if (pool != null)
        {
            // Scrolls and wands use shared mystery sprites
            // (detected by category — we can't import ItemCategory here without circular ref,
            //  so we check pool.GetMysterySprite first, then fall back to AppearancePool constants)
            var mysterySprite = pool.GetMysterySprite(tag.TypeId);
            if (!string.IsNullOrEmpty(mysterySprite))
                return mysterySprite;

            // Scrolls: sprite always "rune_scroll" (45)
            // Wands: sprite always "unknown_wand" (50)
            // These are returned by GetMysterySprite for those categories via the constants.
            // If the pool doesn't have a mystery sprite (scroll or wand), fall back to the
            // category-level constants based on what we know about the item.
            if (item.Has<Logic.Combat.WandComponent>())
                return AppearancePool.WandMysterySprite;

            if (item.Has<Logic.Combat.Consumable>() && item.Has<Logic.Combat.SpellEffect>())
                return AppearancePool.ScrollMysterySprite;
        }

        // Ultimate fallback: use the type ID (shows identified sprite)
        return tag.TypeId;
    }
}
