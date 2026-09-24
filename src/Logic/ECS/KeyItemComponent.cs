namespace UnderWarden.Logic.ECS;

/// <summary>
/// Marks an entity as a colored key item that can unlock a matching LockableComponent.
/// The key is consumed on use — removed from inventory when the chest unlocks.
///
/// LockColorId matches 1:1 with LockableComponent.LockColorId on the target chest.
/// Color IDs 0–4 correspond to red/blue/green/gold/purple (palette defined in DungeonRenderer).
/// </summary>
public sealed class KeyItemComponent : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>
    /// Color ID (0–4) that determines which locked chest this key opens.
    /// Must match the LockableComponent.LockColorId on the target.
    /// 0=red, 1=blue, 2=green, 3=gold, 4=purple.
    /// </summary>
    public int LockColorId { get; set; }
}
