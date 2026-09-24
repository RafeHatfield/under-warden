namespace UnderWarden.Logic.ECS;

/// <summary>
/// Component on consumable items (potions, scrolls, wands, rings) that tracks identification state display.
///
/// Entity.Name is immutable — this component is how the display layer shows the correct name
/// depending on whether the item type has been identified this run.
///
/// Identification state itself lives in IdentificationRegistry (on GameState) — not here.
/// This component just holds the two possible display strings so the registry lookup can
/// choose between them without knowing anything about the item type.
/// </summary>
public sealed class IdentifiableItem : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>
    /// The name shown before identification, e.g. "Fizzy Potion", "Scroll labeled ZELGO MER", "Jade Ring".
    /// Set by AppearancePool when the item is first created. Empty until TASK-003 fills the pool.
    /// </summary>
    public string UnidentifiedName { get; set; } = "";

    /// <summary>
    /// The name shown after identification, e.g. "Healing Potion", "Scroll of Lightning", "Ring of Protection".
    /// Set at item creation time from the definition's DisplayName.
    /// </summary>
    public string IdentifiedName { get; set; } = "";
}
