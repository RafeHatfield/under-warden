using UnderWarden.Logic.Combat;

namespace UnderWarden.Logic.ECS;

/// <summary>
/// Holds items an entity is carrying. Items are Entity instances with Consumable/Equippable components.
/// Enforces a capacity limit and stacks same-named consumables automatically.
/// </summary>
public sealed class Inventory : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>Maximum number of distinct item slots (matching PoC DEFAULT_INVENTORY_CAPACITY).</summary>
    public const int Capacity = 25;

    private readonly List<Entity> _items = new();

    public IReadOnlyList<Entity> Items => _items;
    public int Count => _items.Count;

    public bool IsFull => Count >= Capacity;

    /// <summary>
    /// Try to add an item to the inventory.
    ///
    /// Stacking rule: if the incoming item is a Consumable and an existing inventory
    /// slot already holds a Consumable with the same Name, increment that slot's
    /// StackSize rather than adding a new entry. The incoming entity is discarded.
    ///
    /// Capacity: if no stack match is found and the inventory is full, returns false
    /// and the item is NOT added.
    ///
    /// Returns true on success (either stacked or appended), false when full.
    /// </summary>
    public bool Add(Entity item)
    {
        var incoming = item.Get<Consumable>();

        // Attempt to stack onto an existing same-type consumable slot.
        // Primary matching: ItemTag.TypeId (canonical YAML key, e.g. "healing_potion").
        // Fallback: Entity.Name matching for items without ItemTag (backward compat for tests/scenarios).
        //
        // Using TypeId rather than Name means unidentified items of the same type stack correctly
        // even when their display names differ. Identification state lives in the registry, not on
        // the item, so identifying a type does not break existing stacks.
        if (incoming != null)
        {
            var newTag = item.Get<ItemTag>();

            var existing = _items.FirstOrDefault(i =>
            {
                if (i.Get<Consumable>() == null) return false;
                var existingTag = i.Get<ItemTag>();
                if (newTag != null && existingTag != null)
                    return existingTag.TypeId == newTag.TypeId;
                // Fallback: name-based matching for untagged items (legacy / hand-constructed in tests)
                return i.Name == item.Name;
            });

            if (existing != null)
            {
                existing.Require<Consumable>().StackSize += incoming.StackSize;
                return true;
            }
        }

        // No stack match — check capacity before adding a new slot
        if (IsFull)
            return false;

        _items.Add(item);
        return true;
    }

    public bool Remove(Entity item) => _items.Remove(item);

    /// <summary>Find the first item matching a predicate.</summary>
    public Entity? FindFirst(Func<Entity, bool> predicate) => _items.FirstOrDefault(predicate);
}
