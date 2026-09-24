using UnderWarden.Logic.Balance;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for the depth boon system — Phase 1 of overnight build.
///
/// Covers:
/// - Each of the 5 boons applies correct stat mutations (matching PoC exactly)
/// - First-visit awards boon; return visit does not
/// - BoonTracker state (visited depths, boons applied, reset, disable)
/// - Fighter.BoonMaxHpBonus affects MaxHp and stacks with RingMaxHpBonus
/// - BoonMaxHpBonus persists across floor transitions via PlayerCarryForward
/// - ContentLoader loads all 5 boons from config/depth_boons.yaml
/// - All 5 boons applied sequentially produce correct cumulative stats
/// </summary>
[TestFixture]
public class DepthBoonTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Standard boon table matching config/depth_boons.yaml / PoC DEPTH_BOON_MAP.</summary>
    private static Dictionary<int, BoonDefinition> StandardBoonTable() => new()
    {
        [1] = new BoonDefinition("fortitude_10", "Fortitude", "+10 max HP", HpBonus: 10, ImmediateHeal: 10),
        [2] = new BoonDefinition("accuracy_1", "Keen Eye", "+2 accuracy", AccuracyBonus: 2),
        [3] = new BoonDefinition("defense_1", "Iron Skin", "+1 defense", DefenseBonus: 1),
        [4] = new BoonDefinition("damage_1", "Cruel Blow", "+1 min damage", MinDamageBonus: 1),
        [5] = new BoonDefinition("resilience_5", "Resilience", "+10 max HP", HpBonus: 10, ImmediateHeal: 10),
    };

    private static Entity CreatePlayer(int hp = 80, int accuracy = 5, int defense = 0,
        int damageMin = 3, int damageMax = 5)
    {
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: hp, accuracy: accuracy, defense: defense,
            damageMin: damageMin, damageMax: damageMax));
        return player;
    }

    // ─── Individual boon application ─────────────────────────────────────────

    [Test]
    public void ApplyBoon_Fortitude_AddsMaxHp_And_Heals()
    {
        var player = CreatePlayer(hp: 80);
        var fighter = player.Require<Fighter>();
        fighter.Hp = 70; // wounded

        var boon = StandardBoonTable()[1]; // fortitude_10
        BoonSystem.ApplyBoon(player, boon);

        Assert.That(fighter.BoonMaxHpBonus, Is.EqualTo(10));
        Assert.That(fighter.MaxHp, Is.EqualTo(90)); // 80 base + 10 boon
        Assert.That(fighter.Hp, Is.EqualTo(80)); // 70 + 10 heal, capped at 90
    }

    [Test]
    public void ApplyBoon_KeenEye_AddsAccuracy()
    {
        var player = CreatePlayer(accuracy: 5);
        var fighter = player.Require<Fighter>();

        BoonSystem.ApplyBoon(player, StandardBoonTable()[2]);

        Assert.That(fighter.Accuracy, Is.EqualTo(7)); // 5 + 2
    }

    [Test]
    public void ApplyBoon_IronSkin_AddsDefense()
    {
        var player = CreatePlayer(defense: 0);
        var fighter = player.Require<Fighter>();

        BoonSystem.ApplyBoon(player, StandardBoonTable()[3]);

        Assert.That(fighter.BaseDefense, Is.EqualTo(1)); // 0 + 1
    }

    [Test]
    public void ApplyBoon_CruelBlow_AddsDamageMin()
    {
        var player = CreatePlayer(damageMin: 3);
        var fighter = player.Require<Fighter>();

        BoonSystem.ApplyBoon(player, StandardBoonTable()[4]);

        Assert.That(fighter.DamageMin, Is.EqualTo(4)); // 3 + 1
    }

    [Test]
    public void ApplyBoon_Resilience_AddsMaxHp_And_Heals()
    {
        var player = CreatePlayer(hp: 80);
        var fighter = player.Require<Fighter>();
        fighter.Hp = 60; // wounded

        BoonSystem.ApplyBoon(player, StandardBoonTable()[5]);

        Assert.That(fighter.BoonMaxHpBonus, Is.EqualTo(10));
        Assert.That(fighter.MaxHp, Is.EqualTo(90));
        Assert.That(fighter.Hp, Is.EqualTo(70)); // 60 + 10 heal
    }

    // ─── Eligibility ─────────────────────────────────────────────────────────

    [Test]
    public void ApplyDepthBoon_FirstVisit_AppliesBoon()
    {
        var player = CreatePlayer();
        var tracker = new BoonTracker();
        var table = StandardBoonTable();

        var result = BoonSystem.ApplyDepthBoonIfEligible(player, 1, tracker, table);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.BoonId, Is.EqualTo("fortitude_10"));
        Assert.That(tracker.VisitedDepths, Contains.Item(1));
        Assert.That(tracker.BoonsApplied, Contains.Item("fortitude_10"));
    }

    [Test]
    public void ApplyDepthBoon_SecondVisit_NoBoon()
    {
        var player = CreatePlayer();
        var tracker = new BoonTracker();
        var table = StandardBoonTable();

        BoonSystem.ApplyDepthBoonIfEligible(player, 1, tracker, table);
        var result = BoonSystem.ApplyDepthBoonIfEligible(player, 1, tracker, table);

        Assert.That(result, Is.Null);
        Assert.That(tracker.BoonsApplied, Has.Count.EqualTo(1));
    }

    [Test]
    public void ApplyDepthBoon_Depth6_NoBoon()
    {
        var player = CreatePlayer();
        var tracker = new BoonTracker();
        var table = StandardBoonTable();

        var result = BoonSystem.ApplyDepthBoonIfEligible(player, 6, tracker, table);

        Assert.That(result, Is.Null);
        // Depth 6 is still recorded as visited (matches PoC behavior)
        Assert.That(tracker.VisitedDepths, Contains.Item(6));
    }

    [Test]
    public void ApplyDepthBoon_Disabled_NoBoon()
    {
        var player = CreatePlayer();
        var tracker = new BoonTracker { DisableDepthBoons = true };
        var table = StandardBoonTable();

        var result = BoonSystem.ApplyDepthBoonIfEligible(player, 1, tracker, table);

        Assert.That(result, Is.Null);
        Assert.That(tracker.VisitedDepths, Is.Empty);
        Assert.That(tracker.BoonsApplied, Is.Empty);
    }

    // ─── BoonTracker state ───────────────────────────────────────────────────

    [Test]
    public void BoonTracker_VisitedDepths_Persists()
    {
        var tracker = new BoonTracker();
        tracker.VisitedDepths.Add(1);
        tracker.VisitedDepths.Add(3);

        Assert.That(tracker.VisitedDepths, Has.Count.EqualTo(2));
        Assert.That(tracker.VisitedDepths, Contains.Item(1));
        Assert.That(tracker.VisitedDepths, Contains.Item(3));
    }

    [Test]
    public void BoonTracker_Reset_ClearsEverything()
    {
        var tracker = new BoonTracker();
        tracker.VisitedDepths.Add(1);
        tracker.BoonsApplied.Add("fortitude_10");

        tracker.Reset();

        Assert.That(tracker.VisitedDepths, Is.Empty);
        Assert.That(tracker.BoonsApplied, Is.Empty);
    }

    // ─── Fighter integration ─────────────────────────────────────────────────

    [Test]
    public void Fighter_BoonMaxHpBonus_AffectsMaxHp()
    {
        var fighter = new Fighter(hp: 80);
        Assert.That(fighter.MaxHp, Is.EqualTo(80));

        fighter.BoonMaxHpBonus = 10;
        Assert.That(fighter.MaxHp, Is.EqualTo(90));
    }

    [Test]
    public void Fighter_BoonMaxHpBonus_StacksWithRingMaxHpBonus()
    {
        var fighter = new Fighter(hp: 80);
        fighter.RingMaxHpBonus = 20;
        fighter.BoonMaxHpBonus = 10;

        Assert.That(fighter.MaxHp, Is.EqualTo(110)); // 80 + 20 ring + 10 boon
    }

    // ─── Cumulative application ──────────────────────────────────────────────

    [Test]
    public void AllFiveBoons_AppliedSequentially_CorrectStats()
    {
        var player = CreatePlayer(hp: 80, accuracy: 5, defense: 0, damageMin: 3);
        var fighter = player.Require<Fighter>();
        var tracker = new BoonTracker();
        var table = StandardBoonTable();

        for (int depth = 1; depth <= 5; depth++)
            BoonSystem.ApplyDepthBoonIfEligible(player, depth, tracker, table);

        // fortitude_10: +10 max HP, +10 heal
        // accuracy_1: +2 accuracy
        // defense_1: +1 defense
        // damage_1: +1 damage_min
        // resilience_5: +10 max HP, +10 heal
        Assert.That(fighter.BoonMaxHpBonus, Is.EqualTo(20)); // 10 + 10
        Assert.That(fighter.MaxHp, Is.EqualTo(100)); // 80 + 20
        Assert.That(fighter.Hp, Is.EqualTo(100)); // fully healed by two +10 heals from full
        Assert.That(fighter.Accuracy, Is.EqualTo(7)); // 5 + 2
        Assert.That(fighter.BaseDefense, Is.EqualTo(1)); // 0 + 1
        Assert.That(fighter.DamageMin, Is.EqualTo(4)); // 3 + 1
        Assert.That(tracker.BoonsApplied, Has.Count.EqualTo(5));
    }

    // ─── PlayerCarryForward ──────────────────────────────────────────────────

    [Test]
    public void BoonMaxHpBonus_SurvivesFloorTransition()
    {
        var player = CreatePlayer(hp: 80);
        var fighter = player.Require<Fighter>();
        fighter.BoonMaxHpBonus = 20;
        fighter.Hp = 95; // wounded but above base

        var newPlayer = PlayerCarryForward.Apply(player);
        var newFighter = newPlayer.Require<Fighter>();

        Assert.That(newFighter.BoonMaxHpBonus, Is.EqualTo(20));
        Assert.That(newFighter.MaxHp, Is.EqualTo(100)); // 80 + 20
        Assert.That(newFighter.Hp, Is.EqualTo(95)); // wounds carry forward
    }

    // ─── Content loading ─────────────────────────────────────────────────────

    [Test]
    public void ContentLoader_LoadBoons_AllFivePresent()
    {
        var yaml = File.ReadAllText(FindDepthBoonsYaml());
        var loader = new ContentLoader();
        var boons = loader.LoadBoons(yaml);

        Assert.That(boons, Has.Count.EqualTo(5));
        Assert.That(boons.ContainsKey(1), Is.True);
        Assert.That(boons.ContainsKey(5), Is.True);

        // Spot-check depth 1
        var d1 = boons[1];
        Assert.That(d1.BoonId, Is.EqualTo("fortitude_10"));
        Assert.That(d1.DisplayName, Is.EqualTo("Fortitude"));
        Assert.That(d1.HpBonus, Is.EqualTo(10));
        Assert.That(d1.ImmediateHeal, Is.EqualTo(10));

        // Spot-check depth 4
        var d4 = boons[4];
        Assert.That(d4.BoonId, Is.EqualTo("damage_1"));
        Assert.That(d4.MinDamageBonus, Is.EqualTo(1));
    }

    private static string FindDepthBoonsYaml()
    {
        var testDir = TestContext.CurrentContext.TestDirectory;
        var path = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "config", "depth_boons.yaml"));
        if (File.Exists(path)) return path;
        path = Path.GetFullPath(Path.Combine(testDir, "config", "depth_boons.yaml"));
        if (File.Exists(path)) return path;
        throw new FileNotFoundException($"depth_boons.yaml not found. Tried: {path}");
    }
}
