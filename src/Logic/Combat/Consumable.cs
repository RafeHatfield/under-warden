using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

/// <summary>
/// Makes an entity usable as a consumable item (potions, scrolls).
/// Consumed on use — removed from inventory after application.
/// StackSize tracks how many copies of this consumable are represented by a single
/// inventory entity. When StackSize > 1, using the item decrements the count rather
/// than removing the entity from inventory.
/// </summary>
public sealed class Consumable : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>HP restored on use. 0 for non-healing consumables.</summary>
    public int HealAmount { get; set; }

    /// <summary>
    /// How many of this consumable are stacked on this entity. Runtime-only — not
    /// persisted to YAML. Always >= 1.
    /// </summary>
    public int StackSize { get; set; } = 1;

    /// <summary>
    /// True for potions — bypasses the SilencedEffect gate in ResolveSpellAction.
    /// False for scrolls (default). Drinking a potion while silenced is permitted
    /// because potions are physical (swallowed), not magic (spoken).
    /// </summary>
    public bool IsPotion { get; set; }

    /// <summary>
    /// Turns the player must wait before using another potion (0 = no cooldown). When > 0,
    /// a successful use sets Fighter.PotionCooldownRemaining to this value. Decouples potion
    /// quantity from balance — count doesn't matter, the rate (heal × cooldown) does.
    /// </summary>
    public int UseCooldownTurns { get; set; }

    public Consumable(int healAmount = 0, bool isPotion = false, int useCooldownTurns = 0)
    {
        HealAmount = healAmount;
        IsPotion = isPotion;
        UseCooldownTurns = useCooldownTurns;
    }

    public bool IsHealing => HealAmount > 0;
}
