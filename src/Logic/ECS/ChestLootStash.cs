namespace UnderWarden.Logic.ECS;

/// <summary>
/// Holds the pre-generated loot item entities for a chest.
/// Attached to the chest entity at floor-gen time.
/// TurnController reads this when the chest is opened, dropping all items to FloorItems.
///
/// Loot entities are NOT registered on the map or added to FloorItems until the chest opens.
/// They are stored here in a dormant state.
/// </summary>
public sealed class ChestLootStash : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>The pre-generated loot entities. Empty after the chest has been opened and loot dropped.</summary>
    public List<Entity> Items { get; }

    public ChestLootStash(List<Entity> items)
    {
        Items = items;
    }

    public ChestLootStash() : this(new List<Entity>()) { }
}
