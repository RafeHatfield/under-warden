using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using NUnit.Framework;

namespace UnderWarden.Tests.Logic.Loot;

/// <summary>
/// Tests for LootController: band-aware item generation, density multipliers,
/// category selection, pity interaction, and chest loot.
/// </summary>
[TestFixture]
[Description("LootController: band-aware loot generation system")]
public class LootControllerTests
{
    private LootTagRegistry _tags = null!;
    private LootPolicyConfig _policy = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var lootTagsPath = Path.Combine(testDir, "..", "..", "..", "..", "config", "loot_tags.yaml");
        var lootPolicyPath = Path.Combine(testDir, "..", "..", "..", "..", "config", "loot_policy.yaml");

        Assert.That(File.Exists(lootTagsPath), Is.True, $"loot_tags.yaml not found at {lootTagsPath}");
        Assert.That(File.Exists(lootPolicyPath), Is.True, $"loot_policy.yaml not found at {lootPolicyPath}");

        _tags = LootTagRegistry.FromFile(lootTagsPath);
        _policy = LootPolicyConfig.FromFile(lootPolicyPath);
    }

    /// <summary>
    /// Depth 1 is B1 — density multiplier is 0.35. Over many rooms, approximately 35%
    /// should generate an item. We allow wide tolerance (±15%) since it's probabilistic.
    /// </summary>
    [Test]
    [Description("B1 density roll: ~35% of rooms generate an item")]
    public void B1_DensityMultiplier_ApproximatelyCorrect()
    {
        const int rooms = 2000;
        const int depth = 1;
        var rng = new SeededRandom(1337);

        int itemCount = 0;
        for (int i = 0; i < rooms; i++)
        {
            var item = LootController.GenerateRoomItem(depth, rng, _tags, _policy, pity: null);
            if (item != null) itemCount++;
        }

        double actualRate = (double)itemCount / rooms;
        // Expected: 0.35. Allow ±15% tolerance for probabilistic variance.
        Assert.That(actualRate, Is.InRange(0.20, 0.50),
            $"B1 density rate {actualRate:P1} outside expected [20%, 50%] range");
    }

    /// <summary>
    /// B3 density multiplier is 1.0 — most rooms should generate an item.
    /// At depth 11, almost every room should get loot.
    /// </summary>
    [Test]
    [Description("B3 density roll: ~100% of rooms generate an item")]
    public void B3_FullDensity_MostRoomsGetItem()
    {
        const int rooms = 500;
        const int depth = 11;
        var rng = new SeededRandom(1337);

        int itemCount = 0;
        for (int i = 0; i < rooms; i++)
        {
            var item = LootController.GenerateRoomItem(depth, rng, _tags, _policy, pity: null);
            if (item != null) itemCount++;
        }

        double actualRate = (double)itemCount / rooms;
        Assert.That(actualRate, Is.GreaterThan(0.90),
            $"B3 density rate {actualRate:P1} expected > 90%");
    }

    /// <summary>
    /// At depth 1 (B1), rings should be extremely rare because:
    ///   1. The rare_ev is 0.05 (low EV weight)
    ///   2. The rare multiplier is 0.05 (further reduced to ~0.0025 effective weight)
    /// Over 1000 rooms, rings should appear less than 5% of items found.
    /// </summary>
    [Test]
    [Description("B1 ring rate: rings should be very rare (rare multiplier = 0.05)")]
    public void B1_RingRate_VeryLow()
    {
        const int rooms = 3000;
        const int depth = 1;
        var rng = new SeededRandom(42);

        int totalItems = 0;
        int ringCount = 0;
        var ringIds = new HashSet<string> {
            "ring_of_protection", "ring_of_regeneration", "ring_of_strength", "ring_of_dexterity",
            "ring_of_searching", "ring_of_clarity", "ring_of_constitution", "ring_of_might",
            "ring_of_speed", "ring_of_free_action", "ring_of_luck", "ring_of_hummingbird",
            "ring_of_teleportation", "ring_of_resistance", "ring_of_invisibility",
            "ring_of_wizardry"
        };

        for (int i = 0; i < rooms; i++)
        {
            var item = LootController.GenerateRoomItem(depth, rng, _tags, _policy, pity: null);
            if (item == null) continue;
            totalItems++;
            if (ringIds.Contains(item)) ringCount++;
        }

        double ringRate = totalItems > 0 ? (double)ringCount / totalItems : 0;
        Assert.That(ringRate, Is.LessThan(0.05),
            $"B1 ring rate {ringRate:P1} should be <5% of items; got {ringCount}/{totalItems}");
    }

    /// <summary>
    /// At depth 5 (still B1), no ring should have a band_min that allows B1 rings to spawn.
    /// All common rings are band_min=2, so rings should NEVER appear at depth 1-5 via band filter.
    /// Note: even if rare_ev weight passes, GetItemsForCategory returns empty for B1 rings.
    /// </summary>
    [Test]
    [Description("B1: No rings should appear because all rings are band_min>=2")]
    public void B1_Rings_BandFilteredOut()
    {
        var band = LootBand.B1;
        var ringsInB1 = _tags.GetItemsForCategory("rare", band);

        Assert.That(ringsInB1.Count, Is.EqualTo(0),
            "No rings should be available in B1 (all rings are band_min=2 or higher)");
    }

    /// <summary>
    /// skipDensityRoll=true should always generate an item, even at low-density bands.
    /// Used for dead-end rooms and vault rooms.
    /// </summary>
    [Test]
    [Description("skipDensityRoll: always generates an item regardless of band density")]
    public void SkipDensityRoll_AlwaysGeneratesItem()
    {
        const int rooms = 100;
        const int depth = 1; // B1 — would normally produce ~35% items
        var rng = new SeededRandom(1337);

        int nullCount = 0;
        for (int i = 0; i < rooms; i++)
        {
            var item = LootController.GenerateRoomItem(depth, rng, _tags, _policy, pity: null,
                skipDensityRoll: true);
            if (item == null) nullCount++;
        }

        // With skipDensityRoll=true, no items should be null unless there's truly nothing in the band
        Assert.That(nullCount, Is.EqualTo(0),
            $"skipDensityRoll should guarantee an item; got {nullCount} nulls in {rooms} rooms");
    }

    /// <summary>
    /// forcedCategory should pin selection to the specified category when items are available.
    /// Used for altar rewards (always upgrade_weapon or upgrade_armor).
    /// </summary>
    [Test]
    [Description("forcedCategory pins selection to the specified category")]
    public void ForcedCategory_SelectsFromCorrectCategory()
    {
        const int trials = 50;
        const int depth = 3;
        var rng = new SeededRandom(999);

        // All items generated with forcedCategory=upgrade_weapon should be weapons or enchant_weapon
        var upgradeWeaponItems = _tags
            .GetItemsForCategory("upgrade_weapon", LootBand.B1)
            .Select(t => t.ItemId)
            .ToHashSet();

        for (int i = 0; i < trials; i++)
        {
            var item = LootController.GenerateRoomItem(depth, rng, _tags, _policy, pity: null,
                skipDensityRoll: true, forcedCategory: "upgrade_weapon");

            Assert.That(item, Is.Not.Null, "forcedCategory should always produce an item");
            Assert.That(upgradeWeaponItems.Contains(item!), Is.True,
                $"Item '{item}' is not in upgrade_weapon category for B1");
        }
    }

    /// <summary>
    /// Chest loot should always return at least 1 item (guaranteed, never empty).
    /// </summary>
    [Test]
    [Description("GenerateChestLoot: always returns at least 1 item")]
    public void ChestLoot_NeverEmpty()
    {
        const int trials = 50;
        const int depth = 1;
        var rng = new SeededRandom(1337);

        for (int i = 0; i < trials; i++)
        {
            var loot = LootController.GenerateChestLoot(depth, rng, _tags, _policy, pity: null);
            Assert.That(loot.Count, Is.GreaterThanOrEqualTo(1),
                $"Chest at depth {depth} returned 0 items on trial {i}");
        }
    }

    /// <summary>
    /// Chest loot at depth 11 (B3) should be different from depth 1 (B1) in composition.
    /// B3 has rare_ev=0.5 vs B1=0.05, so rings should appear more often in B3.
    /// </summary>
    [Test]
    [Description("GenerateChestLoot: depth 11 produces more rings than depth 1")]
    public void ChestLoot_DepthAppropriate_RingRatio()
    {
        const int trials = 500;
        var rng1 = new SeededRandom(1337);
        var rng2 = new SeededRandom(1337);

        var ringIds = new HashSet<string> {
            "ring_of_protection", "ring_of_regeneration", "ring_of_strength", "ring_of_dexterity",
            "ring_of_searching", "ring_of_clarity", "ring_of_constitution", "ring_of_might",
            "ring_of_speed", "ring_of_free_action", "ring_of_luck", "ring_of_hummingbird",
            "ring_of_teleportation", "ring_of_resistance", "ring_of_invisibility", "ring_of_wizardry"
        };

        int ringCountB1 = 0, totalB1 = 0;
        int ringCountB3 = 0, totalB3 = 0;

        for (int i = 0; i < trials; i++)
        {
            var lootB1 = LootController.GenerateChestLoot(1, rng1, _tags, _policy, pity: null);
            var lootB3 = LootController.GenerateChestLoot(11, rng2, _tags, _policy, pity: null);

            foreach (var item in lootB1) { totalB1++; if (ringIds.Contains(item)) ringCountB1++; }
            foreach (var item in lootB3) { totalB3++; if (ringIds.Contains(item)) ringCountB3++; }
        }

        double rateB1 = totalB1 > 0 ? (double)ringCountB1 / totalB1 : 0;
        double rateB3 = totalB3 > 0 ? (double)ringCountB3 / totalB3 : 0;

        // B1 rings: 0 (all rings are band_min=2), B3 rings should be significantly higher
        Assert.That(rateB1, Is.EqualTo(0).Within(0.001),
            $"B1 chest ring rate should be 0 (no rings in B1): {rateB1:P1}");
        Assert.That(rateB3, Is.GreaterThan(rateB1),
            $"B3 ring rate {rateB3:P1} should exceed B1 ring rate {rateB1:P1}");
    }

    /// <summary>
    /// LootTagRegistry should have items available for all 8 active categories at B1.
    /// The key category is intentionally empty (DEVIATION-004).
    /// </summary>
    [Test]
    [Description("LootTagRegistry: all active categories have items available in B1")]
    public void Registry_AllActiveCategories_HaveB1Items()
    {
        var categoriesWithB1Items = new[] {
            "healing", "defensive", "panic", "offensive", "utility",
            "upgrade_weapon", "upgrade_armor"
        };

        foreach (var cat in categoriesWithB1Items)
        {
            var items = _tags.GetItemsForCategory(cat, LootBand.B1);
            Assert.That(items.Count, Is.GreaterThan(0),
                $"Category '{cat}' should have items available in B1");
        }

        // key category is empty by design (DEVIATION-004)
        var keyItems = _tags.GetItemsForCategory("key", LootBand.B1);
        Assert.That(keyItems.Count, Is.EqualTo(0),
            "key category should be empty (no keys exist yet, DEVIATION-004)");
    }
}
