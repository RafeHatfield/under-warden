namespace UnderWarden.Logic.ECS;

public sealed class ChestComponent : IComponent
{
    public Entity? Owner { get; set; }

    public bool IsOpen { get; set; }

    /// <summary>True once the player has collected the items from the opened chest (second interaction).</summary>
    public bool IsLooted { get; set; }

    /// <summary>Item type IDs resolved at floor-gen time. Consumed when chest is opened.</summary>
    public List<string> LootItemIds { get; init; } = new();
}
