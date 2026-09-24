using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Generates loot items for a chest at floor-open time.
///
/// Uses the same depth-filtered floor item pool and factory resolution chain as EntityPlacer
/// (SpellItemFactory → ItemFactory → ConsumableFactory) so chest loot is drawn from the
/// same pool as regular floor drops. This keeps the loot feel consistent.
///
/// Count: 2–3 items per chest (initial pass). Depth filtering ensures appropriate loot.
/// Deterministic for a given seed — no ambient RNG usage.
/// </summary>
public static class ChestLootGenerator
{
    private const int MinItems = 2;
    private const int MaxItems = 3;

    /// <summary>
    /// Generate loot entities for a chest.
    ///
    /// Items are created at position (0,0) — callers must set X/Y after creation
    /// (same pattern as EntityPlacer floor drops; position is set by the chest's location).
    ///
    /// Returns an empty list if the pool is empty or all factories are null.
    /// </summary>
    public static List<Entity> Generate(
        int depth,
        SeededRandom rng,
        EntityIdAllocator ids,
        IReadOnlyList<FloorItemPoolEntry> floorItemPool,
        SpellItemFactory? spellItems,
        ItemFactory? items,
        ConsumableFactory? consumables,
        IdentificationRegistry? identRegistry = null,
        AppearancePool? appearancePool = null,
        Difficulty difficulty = Difficulty.Medium)
    {
        var result = new List<Entity>();

        // Depth-filter the pool (same as EntityPlacer.FillRooms).
        // MaxDepth: items age out above this depth (defaults to 99 — no cap).
        var depthPool = floorItemPool
            .Where(e => e.MinDepth <= depth && e.MaxDepth >= depth)
            .ToList();

        if (depthPool.Count == 0)
            return result;

        int count = MinItems + rng.Next(MaxItems - MinItems + 1); // 2 or 3

        for (int i = 0; i < count; i++)
        {
            var entry = SelectWeighted(depthPool, rng);
            if (entry == null) break;

            Entity? entity = null;

            // Resolution chain: SpellItemFactory first (scrolls, wands), then ItemFactory (gear),
            // then ConsumableFactory (potions). Mirrors FillRooms floor-drop resolution.
            if (spellItems != null)
            {
                entity = spellItems.CreateScroll(entry.ItemId,
                             registry: identRegistry, pool: appearancePool,
                             identRng: rng, difficulty: difficulty)
                         ?? spellItems.CreateWand(entry.ItemId, rng, depth,
                             registry: identRegistry, pool: appearancePool,
                             identRng: rng, difficulty: difficulty);
            }

            entity ??= items?.Create(entry.ItemId);

            if (entity == null && consumables != null)
            {
                entity = consumables.Create(entry.ItemId,
                    registry: identRegistry, pool: appearancePool,
                    rng: rng, difficulty: difficulty);
            }

            if (entity == null) continue;

            // Re-wrap with allocator ID (same pattern as EntityPlacer.FillRooms)
            var withId = new Entity(ids.Next(), entity.Name, 0, 0, entity.BlocksMovement);
            CopyComponents(entity, withId);
            result.Add(withId);
        }

        return result;
    }

    /// <summary>
    /// Weighted random selection from the depth-filtered pool.
    /// Returns null only if the pool is empty or all weights are zero.
    /// </summary>
    private static FloorItemPoolEntry? SelectWeighted(List<FloorItemPoolEntry> pool, SeededRandom rng)
    {
        int total = 0;
        foreach (var e in pool) total += e.Weight;
        if (total <= 0) return null;

        int roll = rng.Next(total);
        int cumulative = 0;
        foreach (var e in pool)
        {
            cumulative += e.Weight;
            if (roll < cumulative) return e;
        }
        return pool[^1];
    }

    /// <summary>
    /// Copy all components from source to destination (same pattern as EntityPlacer.CopyComponents).
    /// </summary>
    private static void CopyComponents(Entity source, Entity destination)
    {
        foreach (var component in source.GetAllComponents())
        {
            component.Owner = null;
            destination.Add(component);
        }
    }
}
