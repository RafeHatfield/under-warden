using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;

namespace UnderWarden.Logic.Balance;

/// <summary>
/// Band-aware loot generation system.
///
/// Replaces EntityPlacer's flat 40% pool roll with a two-tier selection:
///   1. Category selection: weighted random pick from band EV table, adjusted for pity
///   2. Item selection: weighted random pick from items available in that category+band
///
/// Design intent (PoC-aligned):
///   - Band multipliers control overall loot density (B1 is sparse)
///   - EV weights control category mix (more healing in B1, more gear in B5)
///   - Healing and rare (ring) weights are further adjusted by band multipliers
///   - Pity ensures no critical category goes dry across a run
///
/// This class is stateless — all run state lives in PityTracker.
/// </summary>
public static class LootController
{
    // Priority order for hard pity checking (healing is survival-critical, armour last)
    private static readonly string[] PityPriority =
        ["healing", "panic", "upgrade_weapon", "upgrade_armor"];

    /// <summary>
    /// Generate 0 or 1 item for a regular procedural room.
    ///
    /// Returns the item_id string, or null if:
    ///   - Density roll fails (no item this room)
    ///   - No items available for the selected category+band
    ///
    /// Flow:
    ///   1. Density roll — skipped if skipDensityRoll is true (dead-end/vault/altar)
    ///   2. Check hard pity (priority: healing > panic > weapon > armor)
    ///   3. Build category weight table from band EVs
    ///   4. Apply healing/rare band multipliers
    ///   5. Apply soft pity bias multipliers
    ///   6. Weighted-random select category
    ///   7. Weighted-random select item within category+band
    ///   8. Notify pity tracker
    /// </summary>
    public static string? GenerateRoomItem(
        int depth,
        SeededRandom rng,
        LootTagRegistry tags,
        LootPolicyConfig policy,
        PityTracker? pity,
        bool skipDensityRoll = false,
        string? forcedCategory = null)
    {
        var band = LootBands.FromDepth(depth);

        // 1. Density roll — chance this room generates any item at all
        if (!skipDensityRoll)
        {
            double densityMultiplier = LootBands.DensityMultiplier[band];
            if (rng.NextDouble() > densityMultiplier)
            {
                // Room is empty this time. Still advance pity counters.
                // (AdvanceRoom is called by the EntityPlacer loop — not here.)
                return null;
            }
        }

        // 2. Check hard pity — forced category overrides all weight logic
        if (pity != null && forcedCategory == null)
        {
            foreach (var pityCategory in PityPriority)
            {
                if (pity.IsHardInjectDue(pityCategory, band, policy))
                {
                    // Hard inject: force this category. Only one per room.
                    pity.ConsumeHardInject(pityCategory);
                    var hardItem = SelectItemFromCategory(pityCategory, band, tags, rng);
                    if (hardItem != null)
                    {
                        pity.RecordRoomItem(pityCategory);
                        return hardItem;
                    }
                    // If no items available in forced category (edge case), fall through to normal
                    break;
                }
            }
        }

        // 3. Build category weight table from band EVs
        var bandEvs = policy.GetBandEvs(band);
        var categoryWeights = new Dictionary<string, double>(bandEvs, StringComparer.OrdinalIgnoreCase);

        // 4. Apply band multipliers to healing/defensive and rare
        double healingMult = LootBands.HealingMultiplier[band];
        double rareMult = LootBands.RareMultiplier[band];

        foreach (var cat in new[] { "healing", "defensive" })
        {
            if (categoryWeights.ContainsKey(cat))
                categoryWeights[cat] *= healingMult;
        }
        if (categoryWeights.ContainsKey("rare"))
            categoryWeights["rare"] *= rareMult;

        // 5. Apply soft pity bias
        if (pity != null)
        {
            foreach (var cat in policy.TrackedCategories)
            {
                if (categoryWeights.ContainsKey(cat))
                {
                    double softMult = pity.GetSoftBiasMultiplier(cat, band, policy);
                    if (softMult > 1.0)
                        categoryWeights[cat] *= softMult;
                }
            }
        }

        // If a forced category was requested (altar bias), pin the selection
        string selectedCategory;
        if (forcedCategory != null)
        {
            selectedCategory = forcedCategory;
        }
        else
        {
            // 6. Weighted-random select category
            // Only consider categories that have at least one item available in this band
            var viableCategories = categoryWeights
                .Where(kv => kv.Value > 0 && tags.GetItemsForCategory(kv.Key, band).Count > 0)
                .ToList();

            if (viableCategories.Count == 0)
                return null;

            selectedCategory = SelectWeightedCategory(viableCategories, rng);
        }

        // 7. Weighted-random select item within category+band
        var item = SelectItemFromCategory(selectedCategory, band, tags, rng);

        // If the selected category has no items (edge case), try other categories
        if (item == null && forcedCategory == null)
        {
            var fallbackCategories = categoryWeights
                .Where(kv => kv.Value > 0 && kv.Key != selectedCategory
                    && tags.GetItemsForCategory(kv.Key, band).Count > 0)
                .OrderByDescending(kv => kv.Value)
                .ToList();

            foreach (var kv in fallbackCategories)
            {
                item = SelectItemFromCategory(kv.Key, band, tags, rng);
                if (item != null)
                {
                    selectedCategory = kv.Key;
                    break;
                }
            }
        }

        if (item == null)
            return null;

        // 8. Notify pity tracker
        pity?.RecordRoomItem(selectedCategory);

        return item;
    }

    /// <summary>
    /// Generate 1-3 items for a chest. Never returns empty.
    ///
    /// Chests skip the density roll — they always generate items.
    /// Item count: 2-3 (biased toward the upper end for depth 10+).
    /// Pity is not advanced for chest items (chests are special rooms, not tracked by pity).
    /// </summary>
    public static List<string> GenerateChestLoot(
        int depth,
        SeededRandom rng,
        LootTagRegistry tags,
        LootPolicyConfig policy,
        PityTracker? pity)
    {
        var band = LootBands.FromDepth(depth);
        var result = new List<string>();

        // Chests contain 2-3 items
        int count = 2 + rng.Next(2); // 2 or 3

        for (int i = 0; i < count; i++)
        {
            // Chest loot skips density roll (chests always have content)
            // Pass null pity — chest items don't reset pity counters (same as PoC treasure rooms)
            var item = GenerateRoomItem(depth, rng, tags, policy, pity: null, skipDensityRoll: true);
            if (item != null)
                result.Add(item);
        }

        // Guarantee at least one item even if generation failed
        if (result.Count == 0)
        {
            // Last resort: pick any item from any category available in this band
            var bandEvs = policy.GetBandEvs(band);
            foreach (var (cat, _) in bandEvs.OrderByDescending(kv => kv.Value))
            {
                var fallback = SelectItemFromCategory(cat, band, tags, rng);
                if (fallback != null)
                {
                    result.Add(fallback);
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Weighted random selection from a category weight list.
    /// The category with the highest total weight is most likely to be selected.
    /// </summary>
    private static string SelectWeightedCategory(
        List<KeyValuePair<string, double>> categories, SeededRandom rng)
    {
        double total = 0;
        foreach (var kv in categories) total += kv.Value;

        double roll = rng.NextDouble() * total;
        double cumulative = 0;
        foreach (var kv in categories)
        {
            cumulative += kv.Value;
            if (roll < cumulative)
                return kv.Key;
        }
        return categories[^1].Key;
    }

    /// <summary>
    /// Weighted random selection of a specific item from a category+band.
    /// Returns null if no items are available in this category at this band.
    /// </summary>
    private static string? SelectItemFromCategory(
        string category, LootBand band, LootTagRegistry tags, SeededRandom rng)
    {
        var items = tags.GetItemsForCategory(category, band);
        if (items.Count == 0)
            return null;

        double total = 0;
        foreach (var t in items) total += t.Weight;
        if (total <= 0)
            return items[0].ItemId;

        double roll = rng.NextDouble() * total;
        double cumulative = 0;
        foreach (var t in items)
        {
            cumulative += t.Weight;
            if (roll < cumulative)
                return t.ItemId;
        }
        return items[^1].ItemId;
    }
}
