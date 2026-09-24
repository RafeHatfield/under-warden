using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Content;

/// <summary>
/// Pre-identification helper: decides whether an item type is identified at the start
/// of a run, before the player encounters it.
///
/// Decision is cached per type per run — the first instance of a type makes the decision,
/// subsequent instances inherit it. This prevents the same type from "flipping" mid-run
/// if a second instance of the same type spawns with a different roll.
///
/// Pre-ID rates by difficulty:
///   | Category | Easy | Medium | Hard |
///   | Potions  |  80% |    50% |   5% |
///   | Scrolls  |  80% |    40% |   5% |
///   | Wands    |  75% |    30% |   0% |
///   | Rings    |  90% |    40% |   0% |
///   | Other    | 100% |   100% | 100% | (weapons/armor always identified)
/// </summary>
public static class PreIdentification
{
    // Pre-identification probability tables keyed by [category][difficulty].
    private static readonly Dictionary<ItemCategory, Dictionary<Difficulty, float>> PreIdRates = new()
    {
        [ItemCategory.Potion] = new()
        {
            [Difficulty.Easy]   = 0.80f,
            [Difficulty.Medium] = 0.50f,
            [Difficulty.Hard]   = 0.05f,
        },
        [ItemCategory.Scroll] = new()
        {
            [Difficulty.Easy]   = 0.80f,
            [Difficulty.Medium] = 0.40f,
            [Difficulty.Hard]   = 0.05f,
        },
        [ItemCategory.Wand] = new()
        {
            [Difficulty.Easy]   = 0.75f,
            [Difficulty.Medium] = 0.30f,
            [Difficulty.Hard]   = 0.00f,
        },
        [ItemCategory.Ring] = new()
        {
            [Difficulty.Easy]   = 0.90f,
            [Difficulty.Medium] = 0.40f,
            [Difficulty.Hard]   = 0.00f,
        },
        [ItemCategory.Other] = new()
        {
            [Difficulty.Easy]   = 1.00f,
            [Difficulty.Medium] = 1.00f,
            [Difficulty.Hard]   = 1.00f,
        },
    };

    /// <summary>
    /// Apply pre-identification decision for an item being created.
    ///
    /// If registry/pool are null (scenario mode, harness), does nothing — all items appear identified.
    ///
    /// Otherwise:
    ///   - If a decision was already made for this type this run: do nothing (inherit cached decision).
    ///   - If no decision yet: roll against the category's pre-ID rate and cache the result.
    ///   - Also sets the IdentifiableItem.UnidentifiedName from the AppearancePool.
    /// </summary>
    public static void Apply(
        Entity item,
        string typeId,
        ItemCategory category,
        IdentificationRegistry? registry,
        AppearancePool? pool,
        SeededRandom rng,
        Difficulty difficulty)
    {
        // Scenario mode or always-identified categories — nothing to do.
        if (registry == null || pool == null || category == ItemCategory.Other)
        {
            // Ensure IdentifiableItem.UnidentifiedName is set even in scenario mode (empty is fine).
            return;
        }

        // Set the unidentified display name from the pool regardless of pre-ID decision.
        // The registry check below determines whether we actually start identified.
        var idComp = item.Get<IdentifiableItem>();
        if (idComp != null)
        {
            string unidName = pool.GetDisplayName(typeId);
            if (!string.IsNullOrEmpty(unidName))
                idComp.UnidentifiedName = unidName;
        }

        // If the decision was already made for this type this run, inherit it.
        if (registry.HasDecision(typeId))
            return;

        // Roll the pre-ID decision once per type per run.
        float rate = GetPreIdRate(category, difficulty);
        if (rng.NextFloat() < rate)
            registry.Identify(typeId);
        else
            registry.MarkUnidentified(typeId);
    }

    /// <summary>
    /// Get the pre-identification probability for a category at the given difficulty.
    /// </summary>
    public static float GetPreIdRate(ItemCategory category, Difficulty difficulty)
    {
        if (PreIdRates.TryGetValue(category, out var rates) &&
            rates.TryGetValue(difficulty, out var rate))
            return rate;

        return 1.0f; // Safe default: identified
    }
}
