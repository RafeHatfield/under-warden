using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using NUnit.Framework;

namespace UnderWarden.Tests.Logic.Loot;

/// <summary>
/// Tests for PityTracker: soft bias activation, hard inject firing, counter resets,
/// and persistence across simulated floor transitions.
/// </summary>
[TestFixture]
[Description("PityTracker: room-based pity system for critical loot categories")]
public class PityTrackerTests
{
    private LootPolicyConfig _policy = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var lootPolicyPath = Path.Combine(testDir, "..", "..", "..", "..", "config", "loot_policy.yaml");

        Assert.That(File.Exists(lootPolicyPath), Is.True, $"loot_policy.yaml not found at {lootPolicyPath}");
        _policy = LootPolicyConfig.FromFile(lootPolicyPath);
    }

    private static PityTracker CreateTracker(LootPolicyConfig policy)
    {
        var tracker = new PityTracker();
        tracker.InitializeTrackedCategories(policy.TrackedCategories);
        return tracker;
    }

    /// <summary>
    /// After soft threshold exceeded, GetSoftBiasMultiplier should return SoftBiasFactor (2.0).
    /// B1 soft threshold for healing is 6 rooms.
    /// </summary>
    [Test]
    [Description("Soft bias activates after soft threshold rooms without healing")]
    public void SoftBias_ActivatesAfterThreshold()
    {
        var tracker = CreateTracker(_policy);
        var band = LootBand.B1;
        var (soft, _) = _policy.GetPityThreshold("healing", band);

        // Before threshold: multiplier should be 1.0
        for (int i = 0; i <= soft; i++)
            tracker.AdvanceRoom();

        // At soft+1 rooms without healing, soft bias should be active
        double multiplier = tracker.GetSoftBiasMultiplier("healing", band, _policy);
        Assert.That(multiplier, Is.EqualTo(_policy.SoftBiasFactor),
            $"Soft bias should be {_policy.SoftBiasFactor}x after {soft+1} rooms without healing");
    }

    /// <summary>
    /// Before soft threshold, multiplier should be 1.0.
    /// </summary>
    [Test]
    [Description("No soft bias before threshold is reached")]
    public void SoftBias_NotActiveBeforeThreshold()
    {
        var tracker = CreateTracker(_policy);
        var band = LootBand.B1;
        var (soft, _) = _policy.GetPityThreshold("healing", band);

        // Advance to exactly the soft threshold (not past it)
        for (int i = 0; i < soft; i++)
            tracker.AdvanceRoom();

        double multiplier = tracker.GetSoftBiasMultiplier("healing", band, _policy);
        Assert.That(multiplier, Is.EqualTo(1.0),
            $"Soft bias should not be active after exactly {soft} rooms");
    }

    /// <summary>
    /// RecordRoomItem should reset the counter and deactivate soft bias.
    /// </summary>
    [Test]
    [Description("RecordRoomItem resets counter and deactivates soft bias")]
    public void RecordRoomItem_ResetsSoftBias()
    {
        var tracker = CreateTracker(_policy);
        var band = LootBand.B1;
        var (soft, _) = _policy.GetPityThreshold("healing", band);

        // Advance past threshold
        for (int i = 0; i <= soft + 1; i++)
            tracker.AdvanceRoom();

        Assert.That(tracker.GetSoftBiasMultiplier("healing", band, _policy),
            Is.EqualTo(_policy.SoftBiasFactor), "Soft bias should be active before reset");

        // Record healing item — should reset
        tracker.RecordRoomItem("healing");

        Assert.That(tracker.GetSoftBiasMultiplier("healing", band, _policy),
            Is.EqualTo(1.0), "Soft bias should deactivate after RecordRoomItem");
    }

    /// <summary>
    /// IsHardInjectDue should fire at the hard threshold (B1 healing = 8 rooms).
    /// </summary>
    [Test]
    [Description("Hard inject fires at hard threshold")]
    public void HardInject_FiresAtHardThreshold()
    {
        var tracker = CreateTracker(_policy);
        var band = LootBand.B1;
        var (_, hard) = _policy.GetPityThreshold("healing", band);

        // Advance to exactly the hard threshold
        for (int i = 0; i < hard; i++)
            tracker.AdvanceRoom();

        Assert.That(tracker.IsHardInjectDue("healing", band, _policy), Is.True,
            $"Hard inject should fire after {hard} rooms without healing");
    }

    /// <summary>
    /// Hard inject should not fire before the threshold.
    /// </summary>
    [Test]
    [Description("Hard inject does not fire before threshold")]
    public void HardInject_NotDueBeforeThreshold()
    {
        var tracker = CreateTracker(_policy);
        var band = LootBand.B1;
        var (_, hard) = _policy.GetPityThreshold("healing", band);

        // Advance to one short of hard threshold
        for (int i = 0; i < hard - 1; i++)
            tracker.AdvanceRoom();

        Assert.That(tracker.IsHardInjectDue("healing", band, _policy), Is.False,
            $"Hard inject should NOT fire after {hard-1} rooms");
    }

    /// <summary>
    /// ConsumeHardInject should clear the pending flag.
    /// </summary>
    [Test]
    [Description("ConsumeHardInject clears the flag after consumption")]
    public void HardInject_ConsumedAndCleared()
    {
        var tracker = CreateTracker(_policy);
        var band = LootBand.B1;
        var (_, hard) = _policy.GetPityThreshold("healing", band);

        // Trigger hard inject
        for (int i = 0; i < hard; i++)
            tracker.AdvanceRoom();

        Assert.That(tracker.IsHardInjectDue("healing", band, _policy), Is.True, "Pre-condition: hard inject due");

        // Consume it
        tracker.ConsumeHardInject("healing");
        tracker.RecordRoomItem("healing");

        // Should no longer be due
        Assert.That(tracker.IsHardInjectDue("healing", band, _policy), Is.False,
            "Hard inject should not be due after consumption + RecordRoomItem");
    }

    /// <summary>
    /// Counters should persist across a simulated floor transition.
    /// The tracker is the same object passed to the new floor.
    /// </summary>
    [Test]
    [Description("Pity counters persist across simulated floor transition")]
    public void Counters_PersistedAcrossFloors()
    {
        var tracker = CreateTracker(_policy);
        var band = LootBand.B1;

        // Simulate 5 rooms on floor 1 without healing
        for (int i = 0; i < 5; i++)
            tracker.AdvanceRoom();

        int roomsSinceBeforeTransition = tracker.RoomsSinceLast("healing");
        Assert.That(roomsSinceBeforeTransition, Is.EqualTo(5));

        // Simulate floor transition — same tracker object passed to floor 2
        // (DungeonFloorBuilder carries pityTracker forward)
        // Just process a couple more rooms on floor 2
        tracker.AdvanceRoom();
        tracker.AdvanceRoom();

        int roomsSinceAfterTransition = tracker.RoomsSinceLast("healing");
        Assert.That(roomsSinceAfterTransition, Is.EqualTo(7),
            "Counters should persist: 5 from floor 1 + 2 from floor 2 = 7");
    }

    /// <summary>
    /// Reset() should clear all state (used when starting a new run).
    /// </summary>
    [Test]
    [Description("Reset clears all counters and flags")]
    public void Reset_ClearsAllState()
    {
        var tracker = CreateTracker(_policy);
        var band = LootBand.B1;
        var (_, hard) = _policy.GetPityThreshold("healing", band);

        // Trigger hard inject for healing
        for (int i = 0; i < hard; i++)
            tracker.AdvanceRoom();

        Assert.That(tracker.IsHardInjectDue("healing", band, _policy), Is.True, "Pre-condition: hard inject due");

        // Reset for new run
        tracker.Reset();
        tracker.InitializeTrackedCategories(_policy.TrackedCategories);

        Assert.That(tracker.IsHardInjectDue("healing", band, _policy), Is.False,
            "Hard inject should not be due after Reset");
        Assert.That(tracker.GetSoftBiasMultiplier("healing", band, _policy), Is.EqualTo(1.0),
            "Soft bias should be inactive after Reset");
        Assert.That(tracker.RoomsSinceLast("healing"), Is.EqualTo(0),
            "Counter should be 0 after Reset");
    }

    /// <summary>
    /// Untracked categories (offensive, utility) should return 1.0 multiplier always.
    /// The PoC only tracks healing, panic, upgrade_weapon, upgrade_armor.
    /// </summary>
    [Test]
    [Description("Untracked categories always return 1.0 multiplier (offensive, utility)")]
    public void UntrackedCategories_NoSoftBias()
    {
        var tracker = CreateTracker(_policy);
        var band = LootBand.B1;

        // Advance many rooms without offensive/utility
        for (int i = 0; i < 20; i++)
            tracker.AdvanceRoom();

        Assert.That(tracker.GetSoftBiasMultiplier("offensive", band, _policy), Is.EqualTo(1.0),
            "offensive is not tracked — should always be 1.0");
        Assert.That(tracker.GetSoftBiasMultiplier("utility", band, _policy), Is.EqualTo(1.0),
            "utility is not tracked — should always be 1.0");
        Assert.That(tracker.IsHardInjectDue("offensive", band, _policy), Is.False,
            "offensive is not tracked — should never have hard inject");
    }

    /// <summary>
    /// LootPolicyConfig should have thresholds for all 5 bands.
    /// Validate the YAML loaded correctly with expected values.
    /// </summary>
    [Test]
    [Description("LootPolicyConfig: pity thresholds load correctly from YAML")]
    public void PolicyConfig_PityThresholds_LoadCorrectly()
    {
        // B1: soft=6, hard=8 (from PoC)
        var (b1Soft, b1Hard) = _policy.GetPityThreshold("healing", LootBand.B1);
        Assert.That(b1Soft, Is.EqualTo(6), "B1 soft pity threshold should be 6");
        Assert.That(b1Hard, Is.EqualTo(8), "B1 hard pity threshold should be 8");

        // B3+: soft=4, hard=6
        var (b3Soft, b3Hard) = _policy.GetPityThreshold("healing", LootBand.B3);
        Assert.That(b3Soft, Is.EqualTo(4), "B3 soft pity threshold should be 4");
        Assert.That(b3Hard, Is.EqualTo(6), "B3 hard pity threshold should be 6");

        // Soft bias factor
        Assert.That(_policy.SoftBiasFactor, Is.EqualTo(2.0), "Soft bias factor should be 2.0");

        // Tracked categories: exactly the 4 PoC categories
        var tracked = _policy.TrackedCategories.ToHashSet();
        Assert.That(tracked.SetEquals(new[] { "healing", "panic", "upgrade_weapon", "upgrade_armor" }),
            Is.True, $"Expected 4 tracked categories; got: {string.Join(", ", tracked)}");
    }
}
