namespace UnderWarden.Logic.ECS;

/// <summary>
/// Marks a chest entity as locked by a colored lock.
/// Only a KeyItemComponent with matching LockColorId can unlock it.
///
/// When IsLocked is true, TurnController refuses to open the chest and emits
/// ChestLockedEvent instead. When the player uses a matching key, IsLocked is set
/// to false and the chest opens normally.
/// </summary>
public sealed class LockableComponent : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>
    /// Color ID (0–4) identifying which key opens this lock.
    /// Must match KeyItemComponent.LockColorId of the key.
    /// 0=red, 1=blue, 2=green, 3=gold, 4=purple.
    /// </summary>
    public int LockColorId { get; set; }

    /// <summary>True until a matching key is used; false once unlocked.</summary>
    public bool IsLocked { get; set; } = true;
}
