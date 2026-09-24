using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Spawns equipment on monsters based on their MonsterEquipmentConfig.
/// Rolls per-slot spawn chances and selects from weighted item pools.
/// Deterministic — same seed produces same equipment loadout.
/// </summary>
public sealed class MonsterEquipmentSpawner
{
    private readonly ItemFactory _itemFactory;

    public MonsterEquipmentSpawner(ItemFactory itemFactory)
    {
        _itemFactory = itemFactory;
    }

    /// <summary>
    /// Roll equipment for a monster entity based on its definition's equipment config.
    /// Creates and equips items. Does nothing if config is null.
    /// </summary>
    public void SpawnEquipment(Entity monster, MonsterEquipmentConfig? config, SeededRandom rng)
    {
        if (config == null) return;

        var equipment = monster.Get<Equipment>() ?? monster.Add(new Equipment());

        foreach (var (slotName, chance) in config.SpawnChances)
        {
            if (rng.NextDouble() >= chance)
                continue;

            if (!config.EquipmentPool.TryGetValue(slotName, out var pool) || pool.Count == 0)
                continue;

            var itemId = SelectWeighted(pool, rng);
            if (itemId == null) continue;

            var item = _itemFactory.Create(itemId);
            if (item == null) continue;

            var slot = ParseSlot(slotName);
            equipment.SetSlot(slot, item);
        }
    }

    /// <summary>
    /// Select an item ID from a weighted pool using the RNG.
    /// </summary>
    private static string? SelectWeighted(List<WeightedItem> pool, SeededRandom rng)
    {
        int totalWeight = 0;
        foreach (var entry in pool)
            totalWeight += entry.Weight;

        if (totalWeight <= 0) return null;

        int roll = rng.Next(totalWeight);
        int cumulative = 0;
        foreach (var entry in pool)
        {
            cumulative += entry.Weight;
            if (roll < cumulative)
                return entry.Item;
        }

        return pool[^1].Item; // shouldn't reach here, but safety fallback
    }

    private static EquipmentSlot ParseSlot(string slot) => slot switch
    {
        "main_hand" => EquipmentSlot.MainHand,
        "off_hand" => EquipmentSlot.OffHand,
        "head" => EquipmentSlot.Head,
        "chest" => EquipmentSlot.Chest,
        "feet" => EquipmentSlot.Feet,
        _ => EquipmentSlot.MainHand,
    };
}
