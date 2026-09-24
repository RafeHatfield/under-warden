namespace UnderWarden.Logic.ECS;

/// <summary>
/// Identifies an item entity's type — the YAML key it was created from (e.g. "shortsword", "healing_potion").
/// Added at creation time by ItemFactory. Used by ItemSpriteManager for sprite lookup.
///
/// Replaces the fragile Name.ToLower().Replace(' ', '_') heuristic that breaks when display name
/// diverges from YAML key (e.g. "short_sword" key vs "Short Sword" display name).
///
/// Analogous to SpeciesTag on monster entities.
/// </summary>
public sealed class ItemTag : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>The YAML item type ID (e.g. "shortsword", "healing_potion", "ring_of_speed").</summary>
    public string TypeId { get; }

    public ItemTag(string typeId)
    {
        TypeId = typeId;
    }
}
